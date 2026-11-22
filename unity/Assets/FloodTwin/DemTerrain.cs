using System;
using System.Collections;
using System.IO;
using System.Runtime.Serialization;
using System.Xml;
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
        private UnityWebRequest request;
        private bool loading;
        private string error;
        private GUIStyle statusStyle;

        internal GridMetadata Grid { get; private set; }

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
            catch (Exception exception) when (exception is InvalidDataException or SerializationException or XmlException)
            {
                Fail($"Could not read the terrain data. {exception.Message}");
                return;
            }

            GetComponent<MeshFilter>().sharedMesh = nextMesh;
            if (mesh != null)
                Destroy(mesh);
            mesh = nextMesh;
            Grid = dem.Grid;

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
                camera.orthographicSize = Mathf.Max(1f, 1.12f * Mathf.Max(bounds.extents.z,
                    bounds.extents.x / Mathf.Max(0.1f, camera.aspect)));
                camera.farClipPlane = Mathf.Max(camera.farClipPlane, height + bounds.size.y + 1f);
            }

            if (overlay == null)
                overlay = gameObject.AddComponent<DemOverlay>();
            overlay.Show(dem, domainUrl);

            Debug.Log($"Loaded {(useMockFixture ? "mock" : "live")} DEM {Grid.DomainId}: " +
                $"{Grid.Cols} × {Grid.Rows}, {Grid.CellSizeM:g} m cells, EPSG:{Grid.Srid}, " +
                $"{dem.ElevationM.Length - mesh.vertexCount:N0} NoData cells.", this);
        }

        private void Fail(string message)
        {
            error = message;
            Debug.LogWarning(message, this);
        }

        // Debug feedback for loading and retrying the terrain during integration.
        private void OnGUI()
        {
            if (!loading && error == null)
                return;

            statusStyle ??= new GUIStyle(GUI.skin.label) { wordWrap = true };
            GUILayout.BeginArea(new Rect(12, 12, Mathf.Max(1, Mathf.Min(480, Screen.width - 24)), 136), GUI.skin.box);
            GUILayout.Label(loading ? "Loading live terrain…" : error, statusStyle);
            if (!loading && GUILayout.Button("Retry"))
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
