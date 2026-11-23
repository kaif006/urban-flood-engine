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
        private const int HotspotsPerPage = 5;
        private static readonly string[] HotspotLabels = { "Off", "Top 1%", "Top 5%", "Top 10%" };
        private static readonly int[] HotspotPercents = { 0, 1, 5, 10 };
        private static readonly int HotspotsId = Shader.PropertyToID("_Hotspots");
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
        private Texture2D legendRamp;
        private Color32[] overlayPixels;
        private FlowHotspots hotspots;
        private int hotspotFilter;
        private int hotspotPage;
        private int appliedHotspotFilter;
        private int appliedHotspotSector;

        internal FlowGrid Flow { get; private set; }

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
            overlayPixels = pixels;
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

        internal void DrawControls()
        {
            if (terrain == null)
                return;

            DemoHud.Section("Flow accumulation");
            if (loading || error != null)
            {
                GUILayout.Label(loading ? "Loading flow and sectors…" : error, DemoHud.Body);
                if (!loading && GUILayout.Button("Retry overlays", DemoHud.Button))
                    Load();
            }
            else if (overlayMaterial != null)
            {
                GUILayout.Label(domainUrl == null ? "Contributing cells · Mock data" : "Contributing cells",
                    DemoHud.Caption);
                int sector = GUILayout.SelectionGrid(selectedSector, SectorLabels, 1, DemoHud.Tab);
                bool visible = GUILayout.Toggle(showFlow, showFlow ? "Overlay on" : "Overlay off", DemoHud.Button);
                if (visible != showFlow || sector != selectedSector)
                {
                    showFlow = visible;
                    selectedSector = sector;
                    ApplySelection();
                }

                if (legendRamp == null)
                    legendRamp = CreateLegendRamp();

                GUILayout.Space(6);
                DemoHud.FlowRamp(peak, legendRamp);
            }
        }

        internal FlowHotspot? DrawHotspots()
        {
            if (overlayMaterial == null)
                return null;

            if (Event.current.type == EventType.Layout &&
                (appliedHotspotFilter != hotspotFilter || appliedHotspotSector != selectedSector))
            {
                appliedHotspotFilter = hotspotFilter;
                appliedHotspotSector = selectedSector;
                hotspotPage = 0;
                hotspots = null;
                if (hotspotFilter != 0)
                {
                    hotspots = FlowHotspots.Apply(Flow.Counts, overlayPixels, terrain.Grid.Cols,
                        selectedSector, HotspotPercents[hotspotFilter]);
                    texture.SetPixels32(overlayPixels);
                    texture.Apply(updateMipmaps: false);
                }
                overlayMaterial.SetFloat(HotspotsId, hotspotFilter == 0 ? 0f : 1f);
            }

            DemoHud.Section("Flow hotspots");
            int filter = GUILayout.SelectionGrid(hotspotFilter, HotspotLabels, 4, DemoHud.Tab);
            if (filter != hotspotFilter)
            {
                hotspotFilter = filter;
                if (filter != 0 && !showFlow)
                {
                    showFlow = true;
                    ApplySelection();
                }
            }

            if (hotspots == null)
                return null;
            if (hotspots.Groups.Count == 0)
            {
                GUILayout.Label("No valid cells in this selection.", DemoHud.Caption);
                return null;
            }

            GUILayout.Label($"{hotspots.CellCount:N0} cells at or above {hotspots.Cutoff:N0} contributing cells",
                DemoHud.Caption);
            GUILayout.Label($"{hotspots.Groups.Count:N0} hotspots · Ties included", DemoHud.Caption);

            int first = hotspotPage * HotspotsPerPage;
            int end = Math.Min(first + HotspotsPerPage, hotspots.Groups.Count);
            FlowHotspot? selected = null;
            for (int i = first; i < end; i++)
            {
                var group = hotspots.Groups[i];
                if (GUILayout.Button($"#{i + 1:N0}  ·  Peak {group.PeakCount:N0}  ·  {group.CellCount:N0} cells",
                    DemoHud.Button))
                    selected = group;
            }

            int pages = (hotspots.Groups.Count + HotspotsPerPage - 1) / HotspotsPerPage;
            if (pages > 1)
            {
                using (new GUILayout.HorizontalScope())
                {
                    int nextPage = hotspotPage;
                    bool enabled = GUI.enabled;
                    GUI.enabled = enabled && hotspotPage > 0;
                    if (GUILayout.Button("Previous", DemoHud.Tab))
                        nextPage--;
                    GUI.enabled = enabled;
                    GUILayout.Label($"{hotspotPage + 1:N0} / {pages:N0}", DemoHud.Caption);
                    GUI.enabled = enabled && hotspotPage + 1 < pages;
                    if (GUILayout.Button("Next", DemoHud.Tab))
                        nextPage++;
                    GUI.enabled = enabled;
                    hotspotPage = nextPage;
                }
            }
            return selected;
        }

        private static Texture2D CreateLegendRamp()
        {
            // Match the linear RGB stops and interpolation in FlowOverlay.shader.
            Color low = new(0.06f, 0.18f, 0.38f);
            Color mid = new(0.02f, 0.62f, 0.69f);
            Color high = new(1f, 0.76f, 0.14f);
            var ramp = new Texture2D(129, 1, TextureFormat.RGBA32, false, true)
            {
                wrapMode = TextureWrapMode.Clamp
            };
            for (int x = 0; x < ramp.width; x++)
            {
                float t = x / 64f;
                ramp.SetPixel(x, 0, t <= 1f ? Color.Lerp(low, mid, t) : Color.Lerp(mid, high, t - 1f));
            }
            ramp.Apply(false, true);
            return ramp;
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
            overlayPixels = null;
            hotspots = null;
            hotspotFilter = 0;
            appliedHotspotFilter = 0;
            appliedHotspotSector = selectedSector;
            hotspotPage = 0;
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

        private void OnDestroy()
        {
            if (legendRamp != null)
                Destroy(legendRamp);
        }
    }
}
