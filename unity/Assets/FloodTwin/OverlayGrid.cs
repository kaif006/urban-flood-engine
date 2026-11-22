using System;
using System.IO;
using System.Runtime.Serialization;
using UnityEngine;

namespace FloodTwin
{
    [DataContract]
    internal sealed class FlowGrid
    {
        [DataMember(Name = "grid", IsRequired = true)] public GridMetadata Grid { get; private set; }
        [DataMember(Name = "direction", IsRequired = true)] public string[] Direction { get; private set; }
        [DataMember(Name = "contributingCellCount", IsRequired = true)] public int?[] Counts { get; private set; }
    }

    [DataContract]
    internal sealed class SectorGrid
    {
        [DataMember(Name = "grid", IsRequired = true)] public GridMetadata Grid { get; private set; }
        [DataMember(Name = "sectors", IsRequired = true)] public SectorSelection[] Sectors { get; private set; }
    }

    [DataContract]
    internal sealed class SectorSelection
    {
        [DataMember(Name = "sectorId", IsRequired = true)] public string Id { get; private set; }
        [DataMember(Name = "cellIndices", IsRequired = true)] public int[] Indices { get; private set; }
        [DataMember(Name = "contributingCellCount", IsRequired = true)] public int[] Counts { get; private set; }
    }

    internal static class OverlayGrid
    {
        public const string Iqbal = "gulshan_e_iqbal";
        public const string Jauhar = "gulistan_e_jauhar";

        public static Color32[] Pack(DemGrid terrain, FlowGrid flow, SectorGrid sectors, out int peak)
        {
            var grid = terrain.Grid;
            int length = terrain.ElevationM.Length;
            if (!grid.Matches(flow.Grid) || !grid.Matches(sectors.Grid))
                throw new InvalidDataException("Overlay grid metadata differs from the loaded terrain.");
            if (flow.Counts?.Length != length || flow.Direction?.Length != length)
                throw new InvalidDataException("Flow arrays do not match the terrain dimensions.");

            var pixels = new Color32[length];
            peak = 0;
            for (int i = 0; i < length; i++)
            {
                if (!terrain.ElevationM[i].HasValue)
                {
                    if (flow.Counts[i].HasValue || flow.Direction[i] != null)
                        throw new InvalidDataException($"Flow and terrain NoData masks differ at cell {i}.");
                    continue;
                }

                if (flow.Counts[i] is not int count || count < 1 || count > length ||
                    string.IsNullOrEmpty(flow.Direction[i]))
                    throw new InvalidDataException($"Invalid flow data at cell {i}.");

                peak = Math.Max(peak, count);
            }

            // Texture row zero represents north. The shader uses -localZ to sample it.
            float logPeak = peak > 1 ? Mathf.Log(peak) : 1f;
            for (int i = 0; i < length; i++)
            {
                if (flow.Counts[i] is int count)
                    pixels[i] = new Color32((byte)Mathf.RoundToInt(255f * Mathf.Log(count) / logPeak),
                        0, 0, 255);
            }

            if (sectors.Sectors?.Length != 2)
                throw new InvalidDataException("Expected both Karachi sector reports.");
            var iqbal = Array.Find(sectors.Sectors, sector => sector?.Id == Iqbal);
            var jauhar = Array.Find(sectors.Sectors, sector => sector?.Id == Jauhar);
            MarkSector(iqbal, flow.Counts, pixels, useGreen: true);
            MarkSector(jauhar, flow.Counts, pixels, useGreen: false);
            return pixels;
        }

        private static void MarkSector(SectorSelection sector, int?[] counts, Color32[] pixels, bool useGreen)
        {
            if (sector?.Indices == null || sector.Counts?.Length != sector.Indices.Length)
                throw new InvalidDataException("The sector report is missing indices or counts.");

            int previous = -1;
            for (int i = 0; i < sector.Indices.Length; i++)
            {
                int index = sector.Indices[i];
                if (index <= previous || index >= counts.Length || counts[index] != sector.Counts[i])
                    throw new InvalidDataException($"Sector {sector.Id} has an invalid or mismatched cell at {index}.");

                if (useGreen)
                    pixels[index].g = 255;
                else
                    pixels[index].b = 255;
                previous = index;
            }
        }
    }
}
