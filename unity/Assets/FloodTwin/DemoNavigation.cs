using UnityEngine;
using UnityEngine.InputSystem;

namespace FloodTwin
{
    internal sealed class DemoNavigation : MonoBehaviour
    {
        private const float ZoomSensitivity = 0.14f;

        private Camera view;
        private MeshCollider surface;
        private DemGrid dem;
        private DemOverlay overlay;
        private CityPilot city;
        private Vector3 initialPosition;
        private Quaternion initialRotation;
        private float initialSize;
        private Rect originalViewport;
        private Vector2Int viewportScreenSize;
        private Vector2 dockScroll;
        private float initialFarClip;
        private int inspected = -1;

        public void Show(DemGrid grid, Mesh mesh, DemOverlay flow, CityPilot context)
        {
            Clear();
            view = Camera.main;
            if (view == null)
                return;

            dem = grid;
            overlay = flow;
            city = context;
            inspected = -1;
            initialPosition = view.transform.position;
            initialRotation = view.transform.rotation;
            originalViewport = view.rect;
            viewportScreenSize = default;
            dockScroll = Vector2.zero;
            initialFarClip = view.farClipPlane;
            if (surface == null)
                surface = gameObject.AddComponent<MeshCollider>();
            surface.sharedMesh = mesh;
            FitViewport();
            view.orthographicSize = initialSize;
        }

        private void Update()
        {
            if (dem == null || view == null)
                return;

            FitViewport();
            if (Keyboard.current?.rKey.wasPressedThisFrame == true)
                ResetView();

            var mouse = Mouse.current;
            if (mouse == null)
                return;
            var pointer = mouse.position.ReadValue();
            var guiPoint = new Vector2(pointer.x, Screen.height - pointer.y);
            if (DemoHud.DockBounds.Contains(guiPoint) || !view.pixelRect.Contains(pointer))
                return;

            var delta = mouse.delta.ReadValue();
            if (mouse.middleButton.isPressed)
                Pan(delta);
            if (mouse.rightButton.isPressed && delta != Vector2.zero)
                Orbit(delta);

            float scroll = mouse.scroll.ReadValue().y;
            if (scroll != 0)
                view.orthographicSize = Mathf.Clamp(view.orthographicSize * Mathf.Exp(-scroll * ZoomSensitivity),
                    dem.Grid.CellSizeM, initialSize * 2f);

            if (mouse.leftButton.wasPressedThisFrame)
                Inspect(pointer);
        }

        private void FitViewport()
        {
            var screen = new Vector2Int(Screen.width, Screen.height);
            if (screen == viewportScreenSize || screen.x <= 0 || screen.y <= 0)
                return;

            viewportScreenSize = screen;
            float dockWidth = DemoHud.DockBounds.width;
            float left = dockWidth / screen.x;
            view.rect = new Rect(left, 0f, 1f - left, 1f);
            view.aspect = (screen.x - dockWidth) / screen.y;

            var extents = surface.sharedMesh.bounds.extents;
            initialSize = Mathf.Max(1f, 1.12f * Mathf.Max(extents.z,
                extents.x / Mathf.Max(0.1f, view.aspect)));
        }

        private void Pan(Vector2 delta)
        {
            float metresPerPixel = 2f * view.orthographicSize / Mathf.Max(1, view.pixelHeight);
            var up = Vector3.ProjectOnPlane(view.transform.up, Vector3.up).normalized;
            view.transform.position -= (view.transform.right * delta.x + up * delta.y) * metresPerPixel;
        }

        private void Orbit(Vector2 delta)
        {
            var centreRay = view.ViewportPointToRay(new Vector3(0.5f, 0.5f));
            if (!surface.Raycast(centreRay, out var hit, view.farClipPlane))
                return;

            var angles = view.transform.eulerAngles;
            float pitch = Mathf.Clamp(angles.x - delta.y * 0.18f, 20f, 90f);
            var rotation = Quaternion.Euler(pitch, angles.y + delta.x * 0.18f, 0f);
            float halfWidth = view.orthographicSize * Mathf.Max(1f, view.aspect);
            float distance = Mathf.Max(Vector3.Distance(view.transform.position, hit.point), 2f * halfWidth);
            view.transform.SetPositionAndRotation(hit.point - rotation * Vector3.forward * distance, rotation);
            view.farClipPlane = Mathf.Max(view.farClipPlane, distance + surface.bounds.size.magnitude);
        }

        private void Inspect(Vector2 pointer)
        {
            inspected = -1;
            if (!surface.Raycast(view.ScreenPointToRay(pointer), out var hit, view.farClipPlane))
                return;

            var position = transform.InverseTransformPoint(hit.point);
            var grid = dem.Grid;
            int col = Mathf.FloorToInt(position.x / grid.CellSizeM);
            int row = Mathf.FloorToInt(-position.z / grid.CellSizeM);
            if (row < 0 || row >= grid.Rows || col < 0 || col >= grid.Cols)
                return;

            int index = row * grid.Cols + col;
            if (dem.ElevationM[index].HasValue)
                inspected = index;
        }

        private void FocusHotspot(FlowHotspot hotspot)
        {
            city?.RestoreView();

            float cellSize = dem.Grid.CellSizeM;
            float centreX = (hotspot.MinCol + (hotspot.MaxCol - hotspot.MinCol + 1) * 0.5f) * cellSize;
            float centreZ = -(hotspot.MinRow + (hotspot.MaxRow - hotspot.MinRow + 1) * 0.5f) * cellSize;
            var centre = transform.TransformPoint(new Vector3(centreX, 0f, centreZ));
            view.transform.SetPositionAndRotation(new Vector3(centre.x, initialPosition.y, centre.z), initialRotation);
            float width = (hotspot.MaxCol - hotspot.MinCol + 1) * cellSize;
            float depth = (hotspot.MaxRow - hotspot.MinRow + 1) * cellSize;
            view.orthographicSize = Mathf.Max(cellSize, 0.65f * Mathf.Max(depth, width / view.aspect));
            view.farClipPlane = initialFarClip;
            inspected = hotspot.PeakCell;
        }

        private void ResetView()
        {
            city?.RestoreView();
            view.transform.SetPositionAndRotation(initialPosition, initialRotation);
            view.orthographicSize = initialSize;
            view.farClipPlane = initialFarClip;
        }

        private void OnGUI()
        {
            if (dem == null || view == null)
                return;

            DemoHud.BeginDock();
            GUILayout.Label("Karachi Flood Twin", DemoHud.Title);
            dockScroll = GUILayout.BeginScrollView(dockScroll);
            overlay.DrawControls();
            if (overlay.DrawHotspots() is FlowHotspot hotspot)
                FocusHotspot(hotspot);
            if (city != null)
                city.DrawControls();

            DemoHud.Section("Cell inspector");
            if (inspected < 0)
                GUILayout.Label("Select a terrain cell to see its elevation and flow.", DemoHud.Caption);
            else
            {
                int row = inspected / dem.Grid.Cols;
                int col = inspected % dem.Grid.Cols;
                GUILayout.Label($"Row {row}  ·  Column {col}  ·  {dem.ElevationM[inspected]:0.00} m", DemoHud.Body);
                if (overlay.Flow is { } flow && flow.Counts[inspected] is int count)
                    GUILayout.Label($"D8 {flow.Direction[inspected]}  ·  {count:N0} contributing cells", DemoHud.Caption);
                else
                    GUILayout.Label("Flow data is unavailable for this cell.", DemoHud.Caption);
            }
            DemoHud.Section("Map controls");
            if (GUILayout.Button("Reset view  [R]", DemoHud.Button))
                ResetView();
            GUILayout.Label("Middle drag to pan · Right drag to orbit\nScroll to zoom · Click to inspect", DemoHud.Caption);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        public void Clear()
        {
            if (view != null)
            {
                view.rect = originalViewport;
                view.ResetAspect();
            }
            dem = null;
            view = null;
            if (surface != null)
                surface.sharedMesh = null;
        }
    }
}
