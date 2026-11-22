using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Newtonsoft.Json;
using UnityEngine;

namespace FloodTwin
{
    internal sealed class CityPilot : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

        private readonly List<Mesh> meshes = new();
        private bool loading;
        private Material roadMaterial;
        private Material buildingMaterial;
        private Material terrainMaterial;
        private GameObject roadLayer;
        private GameObject buildingLayer;
        private Bounds bounds;
        private Camera focusedCamera;
        private Vector3 previousPosition;
        private Quaternion previousRotation;
        private float previousSize;
        private float previousFarClip;
        private bool showRoads = true;
        private bool showBuildings = true;

        private void Awake() => terrainMaterial = GetComponent<MeshRenderer>().sharedMaterial;

        public void Show(DemGrid dem)
        {
            var asset = Resources.Load<TextAsset>("FloodTwin/CityPilot");
            var roots = gameObject.scene.GetRootGameObjects();
            var roadRoot = Array.Find(roots, root => root.name == "Roads");
            var buildingRoot = Array.Find(roots, root => root.name == "Buildings");
            if (asset == null || roadRoot == null || buildingRoot == null)
            {
                Debug.LogWarning("The city pilot needs its geometry asset and the Roads and Buildings scene roots.", this);
                return;
            }

            PilotGeometry pilot;
            try
            {
                using var stream = new MemoryStream(asset.bytes, writable: false);
                pilot = DemGrid.ReadJson<PilotGeometry>(stream);
            }
            catch (Exception exception) when (exception is InvalidDataException or JsonException)
            {
                Debug.LogWarning($"Could not read the city pilot geometry. {exception.Message}", this);
                return;
            }

            if (pilot.Srid != dem.Grid.Srid || pilot.Extent?.Length != 4)
            {
                Debug.LogWarning("The city pilot extent or CRS does not match the terrain configuration.", this);
                return;
            }

            var surface = new TerrainSurface(dem);
            var min = surface.LocalPoint(pilot.Extent, 0);
            var max = surface.LocalPoint(pilot.Extent, 2);
            var centre = (min + max) * 0.5f;
            surface.TryElevation(centre, out float height);
            bounds = new Bounds(new Vector3(centre.x, height, centre.y),
                new Vector3(max.x - min.x, 30f, max.y - min.y));

            roadMaterial = CreateMaterial("City asphalt", new Color32(77, 87, 95, 255));
            buildingMaterial = CreateMaterial("City masonry", new Color32(224, 195, 153, 255));
            roadLayer = CreateLayer(roadRoot.transform, "Mapped roads", showRoads);
            buildingLayer = CreateLayer(buildingRoot.transform, "Mapped buildings", showBuildings);

            var batches = Resources.LoadAll<TextAsset>("FloodTwin/CityCoverage");
            if (batches.Length == 0)
                AddBatch(surface, pilot, "Gulshan Chowrangi");
            else
                StartCoroutine(LoadCoverage(surface, dem.Grid.Srid, batches));
        }

        private IEnumerator LoadCoverage(TerrainSurface surface, int srid, TextAsset[] batches)
        {
            loading = true;
            foreach (var asset in batches)
            {
                // Yield between batches so loading does not block the scene for the full domain.
                yield return null;
                try
                {
                    using var raw = new MemoryStream(asset.bytes, writable: false);
                    using var gzip = new GZipStream(raw, CompressionMode.Decompress);
                    var batch = DemGrid.ReadJson<PilotGeometry>(gzip);
                    if (batch.Srid != srid)
                        throw new InvalidDataException("Coverage batch CRS differs from the terrain CRS.");

                    AddBatch(surface, batch, asset.name);
                }
                catch (Exception exception) when (exception is InvalidDataException or JsonException)
                {
                    Debug.LogWarning($"Could not load {asset.name}. {exception.Message}", this);
                }
            }
            loading = false;
        }

        private void AddBatch(TerrainSurface surface, PilotGeometry batch, string name)
        {
            if (batch.RoadTriangles.Length > 0)
                AddMesh(roadLayer.transform, PilotMeshes.Roads(surface, batch.RoadTriangles), roadMaterial, name);
            if (batch.Buildings.Length > 0)
                AddMesh(buildingLayer.transform, PilotMeshes.Buildings(surface, batch.Buildings), buildingMaterial, name);
        }

        private void AddMesh(Transform root, Mesh mesh, Material material, string name)
        {
            if (mesh.vertexCount == 0)
            {
                Destroy(mesh);
                return;
            }

            mesh.UploadMeshData(markNoLongerReadable: true);
            meshes.Add(mesh);
            var layer = CreateLayer(root, name, visible: true);
            layer.AddComponent<MeshFilter>().sharedMesh = mesh;
            layer.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        private Material CreateMaterial(string name, Color color)
        {
            var material = new Material(terrainMaterial) { name = name };
            material.SetColor(BaseColorId, color);
            material.SetFloat(SmoothnessId, 0.1f);
            return material;
        }

        private static GameObject CreateLayer(Transform root, string name, bool visible)
        {
            var layer = new GameObject(name);
            layer.transform.SetParent(root, worldPositionStays: false);
            layer.SetActive(visible);
            return layer;
        }

        private void Focus()
        {
            var camera = Camera.main;
            if (camera == null)
                return;

            focusedCamera = camera;
            previousPosition = camera.transform.position;
            previousRotation = camera.transform.rotation;
            previousSize = camera.orthographicSize;
            previousFarClip = camera.farClipPlane;

            var rotation = Quaternion.Euler(55f, 0f, 0f);
            float distance = 2f * bounds.extents.magnitude;
            camera.transform.SetPositionAndRotation(
                transform.TransformPoint(bounds.center) - rotation * Vector3.forward * distance, rotation);
            camera.orthographicSize = 1.12f * Mathf.Max(bounds.extents.z + bounds.extents.y,
                bounds.extents.x / Mathf.Max(0.1f, camera.aspect));
            camera.farClipPlane = Mathf.Max(previousFarClip, distance + bounds.extents.magnitude + 1f);
        }

        internal void RestoreView()
        {
            if (focusedCamera == null)
                return;

            focusedCamera.transform.SetPositionAndRotation(previousPosition, previousRotation);
            focusedCamera.orthographicSize = previousSize;
            focusedCamera.farClipPlane = previousFarClip;
            focusedCamera = null;
        }

        private static Rect Panel => new(12f, Mathf.Max(12f, Screen.height - 157f),
            Mathf.Max(1f, Mathf.Min(276f, Screen.width - 24f)), 145f);

        internal bool CoversPanel(Vector2 point) => roadLayer != null && Panel.Contains(point);

        // Pilot controls share the integration scene's existing debug UI.
        private void OnGUI()
        {
            if (roadLayer == null || buildingLayer == null)
                return;

            GUILayout.BeginArea(Panel, GUI.skin.box);
            GUILayout.Label("Mapped roads and buildings");
            if (loading)
                GUILayout.Label("Loading city geometry…");
            if (GUILayout.Button(focusedCamera == null ? "Focus Gulshan Chowrangi" : "Return to shared view"))
            {
                if (focusedCamera == null)
                    Focus();
                else
                    RestoreView();
            }

            GUILayout.BeginHorizontal();
            bool roadsVisible = GUILayout.Toggle(showRoads, "Roads");
            bool buildingsVisible = GUILayout.Toggle(showBuildings, "Buildings");
            GUILayout.EndHorizontal();
            if (roadsVisible != showRoads || buildingsVisible != showBuildings)
            {
                showRoads = roadsVisible;
                showBuildings = buildingsVisible;
                roadLayer.SetActive(showRoads);
                buildingLayer.SetActive(showBuildings);
            }

            if (GUILayout.Button("© OpenStreetMap contributors · ODbL"))
                Application.OpenURL("https://www.openstreetmap.org/copyright");
            GUILayout.EndArea();
        }

        internal void Clear()
        {
            StopAllCoroutines();
            loading = false;
            RestoreView();
            if (roadLayer != null)
            {
                roadLayer.SetActive(false);
                Destroy(roadLayer);
                roadLayer = null;
            }
            if (buildingLayer != null)
            {
                buildingLayer.SetActive(false);
                Destroy(buildingLayer);
                buildingLayer = null;
            }

            foreach (var mesh in meshes)
                Destroy(mesh);
            meshes.Clear();
            Destroy(roadMaterial);
            Destroy(buildingMaterial);
            roadMaterial = null;
            buildingMaterial = null;
        }

        private void OnDisable() => Clear();
    }
}
