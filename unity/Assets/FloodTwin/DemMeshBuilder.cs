using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace FloodTwin
{
    internal static class DemMeshBuilder
    {
        public static Mesh Build(DemGrid dem)
        {
            var grid = dem.Grid;
            var vertices = new List<Vector3>(dem.ElevationM.Length);
            var vertexByCell = new int[dem.ElevationM.Length];

            for (int i = 0; i < dem.ElevationM.Length; i++)
            {
                if (dem.ElevationM[i] is not float height)
                {
                    vertexByCell[i] = -1;
                    continue;
                }

                if (!float.IsFinite(height))
                    throw new InvalidDataException($"Invalid elevation at grid index {i}.");

                int row = i / grid.Cols;
                int col = i % grid.Cols;
                vertexByCell[i] = vertices.Count;
                vertices.Add(new Vector3((col + 0.5f) * grid.CellSizeM, height,
                    -(row + 0.5f) * grid.CellSizeM));
            }

            var triangles = new List<int>();
            var corners = new int[4];
            for (int row = 0; row < grid.Rows - 1; row++)
            {
                for (int col = 0; col < grid.Cols - 1; col++)
                {
                    int northWest = row * grid.Cols + col;
                    corners[0] = vertexByCell[northWest];
                    corners[1] = vertexByCell[northWest + 1];
                    corners[2] = vertexByCell[northWest + grid.Cols + 1];
                    corners[3] = vertexByCell[northWest + grid.Cols];

                    // Keep the valid corners in clockwise order as viewed from above.
                    int valid = 0;
                    for (int i = 0; i < corners.Length; i++)
                        if (corners[i] >= 0)
                            corners[valid++] = corners[i];

                    for (int i = 1; i < valid - 1; i++)
                    {
                        triangles.Add(corners[0]);
                        triangles.Add(corners[i]);
                        triangles.Add(corners[i + 1]);
                    }
                }
            }

            var mesh = new Mesh { name = "DEM Terrain" };
            if (vertices.Count > ushort.MaxValue)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            return mesh;
        }
    }
}
