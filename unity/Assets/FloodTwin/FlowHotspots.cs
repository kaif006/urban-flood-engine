using System;
using System.Collections.Generic;
using UnityEngine;

namespace FloodTwin
{
    internal readonly struct FlowHotspot
    {
        internal readonly int MinRow, MaxRow, MinCol, MaxCol;
        internal readonly int CellCount, PeakCell, PeakCount;

        internal FlowHotspot(int minRow, int maxRow, int minCol, int maxCol,
            int cellCount, int peakCell, int peakCount)
        {
            MinRow = minRow;
            MaxRow = maxRow;
            MinCol = minCol;
            MaxCol = maxCol;
            CellCount = cellCount;
            PeakCell = peakCell;
            PeakCount = peakCount;
        }
    }

    internal sealed class FlowHotspots
    {
        internal readonly int Cutoff;
        internal readonly int CellCount;
        internal readonly List<FlowHotspot> Groups;

        private FlowHotspots(int cutoff, int cellCount, List<FlowHotspot> groups)
        {
            Cutoff = cutoff;
            CellCount = cellCount;
            Groups = groups;
        }

        // The overlay pixels carry validated sector membership in green and blue.
        // Alpha stays opaque for qualifying cells and marks the dimmed valid cells at 128.
        internal static FlowHotspots Apply(int?[] counts, Color32[] pixels, int cols, int sector, int percent)
        {
            var distribution = new int[counts.Length + 1];
            int eligible = 0;
            int peak = 0;
            for (int i = 0; i < counts.Length; i++)
            {
                if (counts[i] is int count && InSector(pixels[i], sector))
                {
                    distribution[count]++;
                    peak = Math.Max(peak, count);
                    eligible++;
                }
            }

            int target = (eligible * percent + 99) / 100;
            int qualifying = 0;
            int cutoff = peak;
            while (cutoff > 0)
            {
                qualifying += distribution[cutoff];
                if (qualifying >= target)
                    break;
                cutoff--;
            }

            for (int i = 0; i < counts.Length; i++)
            {
                if (counts[i] is int count)
                    pixels[i].a = count >= cutoff && InSector(pixels[i], sector) ? (byte)255 : (byte)128;
            }

            var groups = new List<FlowHotspot>();
            var seen = new bool[counts.Length];
            var queue = new Queue<int>();
            int rows = counts.Length / cols;
            for (int i = 0; i < counts.Length; i++)
            {
                if (pixels[i].a != 255 || seen[i])
                    continue;

                seen[i] = true;
                queue.Enqueue(i);
                int minRow = i / cols, maxRow = minRow;
                int minCol = i % cols, maxCol = minCol;
                int cellCount = 0;
                int peakCell = i;
                int peakCount = counts[i].Value;
                while (queue.Count > 0)
                {
                    int cell = queue.Dequeue();
                    int row = cell / cols;
                    int col = cell % cols;
                    minRow = Math.Min(minRow, row);
                    maxRow = Math.Max(maxRow, row);
                    minCol = Math.Min(minCol, col);
                    maxCol = Math.Max(maxCol, col);
                    cellCount++;

                    int count = counts[cell].Value;
                    if (count > peakCount || (count == peakCount && cell < peakCell))
                    {
                        peakCount = count;
                        peakCell = cell;
                    }

                    for (int y = Math.Max(0, row - 1); y <= Math.Min(rows - 1, row + 1); y++)
                    {
                        for (int x = Math.Max(0, col - 1); x <= Math.Min(cols - 1, col + 1); x++)
                        {
                            int next = y * cols + x;
                            if (!seen[next] && pixels[next].a == 255)
                            {
                                seen[next] = true;
                                queue.Enqueue(next);
                            }
                        }
                    }
                }
                groups.Add(new FlowHotspot(minRow, maxRow, minCol, maxCol, cellCount, peakCell, peakCount));
            }

            groups.Sort((left, right) =>
            {
                int order = right.PeakCount.CompareTo(left.PeakCount);
                if (order == 0)
                    order = right.CellCount.CompareTo(left.CellCount);
                return order != 0 ? order : left.PeakCell.CompareTo(right.PeakCell);
            });
            return new FlowHotspots(cutoff, qualifying, groups);
        }

        private static bool InSector(Color32 pixel, int sector) => sector switch
        {
            1 => pixel.g != 0,
            2 => pixel.b != 0,
            _ => true
        };
    }
}
