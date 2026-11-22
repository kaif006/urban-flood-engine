using System;
using System.Collections;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace FloodTwin
{
    internal sealed class DemOverlay : MonoBehaviour
    {
        private static readonly string[] SectorLabels = { "Shared domain", "Gulshan-e-Iqbal", "Gulistan-e-Jauhar" };
        private static readonly int OverlayId = Shader.PropertyToID("_Overlay");
        private static readonly int GridSizeId = Shader.PropertyToID("_GridSize");
        private static readonly int SectorId = Shader.PropertyToID("_Sector");

        private MeshRenderer terrainRenderer;
        private Material terrainMaterial;
        private Material overlayMaterial;
        private Texture2D texture;
        private UnityWebRequest request;
        private DemGrid terrain;
        private string domainUrl;
        private string error;
        private bool loading;
        private bool showFlow = true;
        private int selectedSector;
        private int peak;
        private GUIStyle errorStyle;

        internal FlowGrid Flow { get; private set; }

        private static float PanelWidth => Mathf.Max(1f, Mathf.Min(430f, Screen.width - 24f));
        private static Rect Panel => new(Screen.width - PanelWidth - 12f, 12f, PanelWidth, 158f);

        internal bool CoversPanel(Vector2 point) => terrain != null && Panel.Contains(point);

        private void Awake()
        {
            terrainRenderer = GetComponent<MeshRenderer>();
            terrainMaterial = terrainRenderer.sharedMaterial;
        }

        private void OnEnable()
        {
            if (terrain != null)
                Load();
        }

        public void Show(DemGrid dem, string url)
        {
            Clear();
            terrain = dem;
            domainUrl = url;
            if (isActiveAndEnabled)
                Load();
        }

        private void Load()
        {
            error = null;
            if (domainUrl != null)
            {
                loading = true;
                StartCoroutine(LoadLive());
                return;
            }

            var fixture = Resources.Load<TextAsset>("FloodTwin/MockOverlay");
            if (fixture == null)
            {
                Fail("The mock overlay fixture could not be found.");
                return;
            }

            var json = fixture.bytes;
            Apply(json, json);
        }

        private IEnumerator LoadLive()
        {
            byte[] flow = null;
            byte[] sectors = null;
            yield return Download("/flow", data => flow = data);
            if (flow == null)
                yield break;

            yield return Download("/sectors", data => sectors = data);
            if (sectors == null)
                yield break;

            loading = false;
            Apply(flow, sectors);
        }

        private IEnumerator Download(string path, Action<byte[]> receive)
        {
            request = UnityWebRequest.Get(domainUrl + path);
            request.timeout = 15;
            UnityWebRequestAsyncOperation operation;
            try
            {
                operation = request.SendWebRequest();
            }
            catch (InvalidOperationException exception)
            {
                ReleaseRequest();
                Fail($"Could not start the {path} request. {exception.Message}");
                yield break;
            }

            yield return operation;
            if (request.result == UnityWebRequest.Result.Success)
                receive(request.downloadHandler.data);
            else
                Fail($"Could not load {path}. {request.error}");
            ReleaseRequest();
        }

        private void Apply(byte[] flowJson, byte[] sectorJson)
        {
            Color32[] pixels;
            FlowGrid flow;
            try
            {
                using var flowStream = new MemoryStream(flowJson, writable: false);
                using var sectorStream = new MemoryStream(sectorJson, writable: false);
                flow = DemGrid.ReadJson<FlowGrid>(flowStream);
                pixels = OverlayGrid.Pack(terrain, flow,
                    DemGrid.ReadJson<SectorGrid>(sectorStream), out peak);
            }
            catch (Exception exception) when (exception is InvalidDataException or JsonException)
            {
                Fail($"Could not apply the overlays. {exception.Message}");
                return;
            }

            var shader = Resources.Load<Shader>("FloodTwin/FlowOverlay");
            if (shader == null || !shader.isSupported)
            {
                Fail("The flow overlay shader is unavailable.");
                return;
            }

            var grid = terrain.Grid;
            texture = new Texture2D(grid.Cols, grid.Rows, TextureFormat.RGBA32, mipChain: false, linear: true)
            {
                name = "Flow and sector grid",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false);
            overlayMaterial = new Material(shader) { name = "Flow overlay" };
            overlayMaterial.SetTexture(OverlayId, texture);
            overlayMaterial.SetVector(GridSizeId,
                new Vector4(grid.Cols * grid.CellSizeM, grid.Rows * grid.CellSizeM, 0f, 0f));
            Flow = flow;
            ApplySelection();
        }

        private void ApplySelection()
        {
            overlayMaterial.SetFloat(SectorId, selectedSector);
            terrainRenderer.sharedMaterial = showFlow ? overlayMaterial : terrainMaterial;
        }

        private void Fail(string message)
        {
            error = message;
            loading = false;
            Debug.LogWarning(message, this);
        }

        // Keep the overlay controls alongside the existing terrain integration feedback.
        private void OnGUI()
        {
            if (terrain == null)
                return;

            GUILayout.BeginArea(Panel, GUI.skin.box);
            if (loading || error != null)
            {
                errorStyle ??= new GUIStyle(GUI.skin.label) { wordWrap = true };
                GUILayout.Label(loading ? "Loading flow and sectors…" : error, errorStyle);
                if (!loading && GUILayout.Button("Retry overlays"))
                    Load();
            }
            else if (overlayMaterial != null)
            {
                GUILayout.Label("Flow accumulation (contributing cells)");
                bool visible = GUILayout.Toggle(showFlow, "Show overlay");
                int sector = GUILayout.SelectionGrid(selectedSector, SectorLabels, PanelWidth < 335f ? 1 : 3);
                if (visible != showFlow || sector != selectedSector)
                {
                    showFlow = visible;
                    selectedSector = sector;
                    ApplySelection();
                }

                GUILayout.Label($"Blue: 1    Yellow: {peak:N0}    Log scale");
                if (domainUrl == null)
                    GUILayout.Label("Mock data and sector masks");
            }
            GUILayout.EndArea();
        }

        private void ReleaseRequest()
        {
            request?.Dispose();
            request = null;
        }

        internal void Clear()
        {
            StopAllCoroutines();
            request?.Abort();
            ReleaseRequest();
            loading = false;
            error = null;
            terrain = null;
            Flow = null;
            if (overlayMaterial != null)
            {
                terrainRenderer.sharedMaterial = terrainMaterial;
                Destroy(overlayMaterial);
                overlayMaterial = null;
            }
            if (texture != null)
            {
                Destroy(texture);
                texture = null;
            }
        }

        private void OnDisable() => Clear();
    }
}
