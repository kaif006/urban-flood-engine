using System;
using System.Collections;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace FloodTwin
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class DemTerrain : MonoBehaviour
    {
        private const string DomainPath = "/api/v1/domains/gulshan_jauhar";

        [SerializeField] private bool useMockFixture;
        [SerializeField] private TextAsset fixture;
        [SerializeField] private string apiBaseUrl = "http://127.0.0.1:5080";

        private Mesh mesh;
        private DemOverlay overlay;
        private CityPilot pilot;
        private DemoNavigation navigation;
        private UnityWebRequest request;
        private bool loading;
        private string error;

        internal GridMetadata Grid { get; private set; }

        private void Awake()
        {
            if (Application.isEditor)
                return;

            // Let desktop loading continue if the window loses focus during startup.
            Application.runInBackground = true;
            var args = Environment.GetCommandLineArgs();
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--mock")
                    useMockFixture = true;
                else if (args[i] == "--api-url" && i + 1 < args.Length)
                    apiBaseUrl = args[++i];
            }
        }

        private void OnEnable() => Load();

        private void Load()
        {
            error = null;
            if (useMockFixture)
            {
                if (fixture == null)
                    Fail("Assign a mock DEM JSON fixture.");
                else
                    Render(fixture.bytes);

                return;
            }

            if (!Uri.TryCreate(apiBaseUrl?.Trim(), UriKind.Absolute, out var baseUri) ||
                (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps) ||
                baseUri.Query.Length != 0 || baseUri.Fragment.Length != 0)
            {
                Fail("Set a valid HTTP or HTTPS API base URL on Terrain.");
                return;
            }

            loading = true;
            StartCoroutine(LoadLive(baseUri.AbsoluteUri.TrimEnd('/') + DomainPath));
        }

        private IEnumerator LoadLive(string domainUrl)
        {
            request = UnityWebRequest.Get(domainUrl + "/terrain");
            request.timeout = 15;
            UnityWebRequestAsyncOperation operation;
            try
            {
                operation = request.SendWebRequest();
            }
            catch (InvalidOperationException exception)
            {
                ReleaseRequest();
                loading = false;
                Fail($"Could not start the terrain request. {exception.Message}");
                yield break;
            }

            yield return operation;
            loading = false;
            if (request.result == UnityWebRequest.Result.Success)
            {
                var json = request.downloadHandler.data;
                ReleaseRequest();
                Render(json, domainUrl);
            }
            else
            {
                string reason = request.error;
                ReleaseRequest();
                Fail($"Could not load live terrain. {reason}");
            }
        }

        private void Render(byte[] json, string domainUrl = null)
        {
            DemGrid dem;
            Mesh nextMesh;
            try
            {
                using var stream = new MemoryStream(json, writable: false);
                dem = DemGrid.Read(stream);
                nextMesh = DemMeshBuilder.Build(dem);
            }
            catch (Exception exception) when (exception is InvalidDataException or JsonException)
            {
                Fail($"Could not read the terrain data. {exception.Message}");
                return;
            }

            GetComponent<MeshFilter>().sharedMesh = nextMesh;
            if (mesh != null)
                Destroy(mesh);
            mesh = nextMesh;
            Grid = dem.Grid;

            pilot?.Clear();
            var camera = Camera.main;
            if (camera != null)
            {
                var bounds = mesh.bounds;
                float height = Mathf.Max(100f, Grid.CellSizeM);
                var target = transform.TransformPoint(bounds.center);
                var top = transform.TransformPoint(bounds.center + Vector3.up * bounds.extents.y);
                camera.transform.SetPositionAndRotation(
                    new Vector3(target.x, top.y + height, target.z),
                    Quaternion.Euler(90f, 0f, 0f));
                camera.orthographic = true;
                camera.farClipPlane = Mathf.Max(camera.farClipPlane, height + bounds.size.y + 1f);
            }

            if (domainUrl != null)
            {
                if (pilot == null)
                    pilot = gameObject.AddComponent<CityPilot>();
                pilot.Show(dem);
            }

            if (overlay == null)
                overlay = gameObject.AddComponent<DemOverlay>();
            overlay.Show(dem, domainUrl);
            if (navigation == null)
                navigation = gameObject.AddComponent<DemoNavigation>();
            navigation.Show(dem, mesh, overlay, pilot);

            Debug.Log($"Loaded {(useMockFixture ? "mock" : "live")} DEM {Grid.DomainId}: " +
                $"{Grid.Cols} × {Grid.Rows}, {Grid.CellSizeM:g} m cells, EPSG:{Grid.Srid}, " +
                $"{dem.ElevationM.Length - mesh.vertexCount:N0} NoData cells.", this);
        }

        private void Fail(string message)
        {
            error = message;
            Debug.LogWarning(message, this);
        }

        private void OnGUI()
        {
            if (!loading && error == null)
                return;

            DemoHud.BeginDock();
            GUILayout.Label("Karachi Flood Twin", DemoHud.Title);
            GUILayout.Label(loading ? "Loading live terrain…" : error, DemoHud.Body);
            if (!loading && GUILayout.Button("Retry", DemoHud.Button))
                Load();
            GUILayout.EndArea();
        }

        private void ReleaseRequest()
        {
            request?.Dispose();
            request = null;
        }

        private void OnDisable()
        {
            navigation?.Clear();
            pilot?.Clear();
            overlay?.Clear();
            StopAllCoroutines();
            request?.Abort();
            ReleaseRequest();
            loading = false;
        }

        private void OnDestroy()
        {
            if (mesh != null)
                Destroy(mesh);
        }
    }
}
