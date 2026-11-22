using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Rendering;

namespace FloodTwin
{
    internal sealed class PilotGeometry
    {
        [JsonProperty("srid", Required = Required.Always)] public int Srid { get; private set; }
        [JsonProperty("extent")] public double[] Extent { get; private set; }
        [JsonProperty("roadTriangles", Required = Required.Always)] public double[] RoadTriangles { get; private set; }
        [JsonProperty("buildings", Required = Required.Always)] public PilotBuilding[] Buildings { get; private set; }
    }

    internal sealed class PilotBuilding
    {
        [JsonProperty("heightM", Required = Required.Always)] public float Height { get; private set; }
        [JsonProperty("roof", Required = Required.Always)] public double[] Roof { get; private set; }
        [JsonProperty("rings", Required = Required.Always)] public double[][] Rings { get; private set; }
    }

    internal sealed class TerrainSurface
    {
        private readonly DemGrid dem;

        public TerrainSurface(DemGrid dem) => this.dem = dem;

        public Vector2 LocalPoint(double[] coordinates, int index) => new(
            (float)(coordinates[index] - dem.Grid.OriginX),
            (float)(coordinates[index + 1] - dem.Grid.OriginY));

        public bool TryElevation(Vector2 point, out float height)
        {
            var grid = dem.Grid;
            float column = point.x / grid.CellSizeM - 0.5f;
            float row = -point.y / grid.CellSizeM - 0.5f;
            int c = Mathf.FloorToInt(column);
            int r = Mathf.FloorToInt(row);
            height = 0;
            if (c < 0 || c >= grid.Cols - 1 || r < 0 || r >= grid.Rows - 1)
                return false;

            int index = r * grid.Cols + c;
            var values = dem.ElevationM;
            if (values[index] is not float nw || values[index + grid.Cols + 1] is not float se)
                return false;

            // Interpolate on the same northwest-to-southeast diagonal as the terrain mesh.
            float x = column - c;
            float y = row - r;
            if (x >= y && values[index + 1] is float ne)
            {
                height = nw + (ne - nw) * x + (se - ne) * y;
                return true;
            }

            if (x < y && values[index + grid.Cols] is float sw)
            {
                height = nw + (sw - nw) * y + (se - sw) * x;
                return true;
            }

            return false;
        }
    }

    internal static class PilotMeshes
    {
        public static Mesh Roads(TerrainSurface terrain, double[] triangles)
        {
            var vertices = new List<Vector3>();
            var indices = new List<int>(triangles.Length / 2);
            var vertexByPosition = new Dictionary<Vector3, int>();
            for (int i = 0; i < triangles.Length; i += 6)
            {
                var a = terrain.LocalPoint(triangles, i);
                var b = terrain.LocalPoint(triangles, i + 2);
                var c = terrain.LocalPoint(triangles, i + 4);
                if (!terrain.TryElevation(a, out float yA) || !terrain.TryElevation(b, out float yB) ||
                    !terrain.TryElevation(c, out float yC))
                    continue;

                const float roadOffset = 0.22f;
                AddRoadVertex(Point(a, yA + roadOffset));
                AddRoadVertex(Point(b, yB + roadOffset));
                AddRoadVertex(Point(c, yC + roadOffset));
            }

            return Build("Road ribbons", vertices, indices);

            void AddRoadVertex(Vector3 position)
            {
                if (!vertexByPosition.TryGetValue(position, out int index))
                {
                    index = vertices.Count;
                    vertexByPosition.Add(position, index);
                    vertices.Add(position);
                }

                indices.Add(index);
            }
        }

        public static Mesh Buildings(TerrainSurface terrain, PilotBuilding[] buildings)
        {
            var vertices = new List<Vector3>();
            foreach (var building in buildings)
            {
                if (!TryRoofElevation(terrain, building, out float roof))
                    continue;

                for (int i = 0; i < building.Roof.Length; i += 2)
                    vertices.Add(Point(terrain.LocalPoint(building.Roof, i), roof));

                foreach (var ring in building.Rings)
                {
                    for (int i = 0; i < ring.Length - 2; i += 2)
                    {
                        var a = terrain.LocalPoint(ring, i);
                        var b = terrain.LocalPoint(ring, i + 2);
                        terrain.TryElevation(a, out float groundA);
                        terrain.TryElevation(b, out float groundB);
                        var lowerA = Point(a, groundA - 0.2f);
                        var lowerB = Point(b, groundB - 0.2f);
                        var upperA = Point(a, roof);
                        var upperB = Point(b, roof);
                        vertices.Add(lowerA);
                        vertices.Add(lowerB);
                        vertices.Add(upperB);
                        vertices.Add(lowerA);
                        vertices.Add(upperB);
                        vertices.Add(upperA);
                    }
                }
            }

            var indices = new List<int>(vertices.Count);
            for (int i = 0; i < vertices.Count; i++)
                indices.Add(i);
            return Build("Building shells", vertices, indices);
        }

        private static bool TryRoofElevation(TerrainSurface terrain, PilotBuilding building, out float roof)
        {
            float highest = float.NegativeInfinity;
            foreach (var ring in building.Rings)
            {
                for (int i = 0; i < ring.Length - 2; i += 2)
                {
                    if (!terrain.TryElevation(terrain.LocalPoint(ring, i), out float ground))
                    {
                        roof = 0;
                        return false;
                    }

                    highest = Mathf.Max(highest, ground);
                }
            }

            roof = highest + building.Height;
            return true;
        }

        private static Vector3 Point(Vector2 point, float height) => new(point.x, height, point.y);

        private static Mesh Build(string name, List<Vector3> vertices, List<int> indices)
        {
            var mesh = new Mesh { name = name };
            if (vertices.Count > ushort.MaxValue)
                mesh.indexFormat = IndexFormat.UInt32;

            mesh.SetVertices(vertices);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateNormals();
            return mesh;
        }
    }
}
