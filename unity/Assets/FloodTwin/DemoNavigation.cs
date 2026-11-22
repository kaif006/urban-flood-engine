using UnityEngine;
using UnityEngine.InputSystem;

namespace FloodTwin
{
    internal sealed class DemoNavigation : MonoBehaviour
    {
        private static Rect Panel => new(12f, 12f, Mathf.Max(1f, Mathf.Min(Screen.width - 24f, 320f)), 135f);

        private Camera view;
        private MeshCollider surface;
        private DemGrid dem;
        private DemOverlay overlay;
        private CityPilot city;
        private Vector3 initialPosition;
        private Quaternion initialRotation;
        private float initialSize;
        private float initialFarClip;
        private int inspected = -1;

        public void Show(DemGrid grid, Mesh mesh, DemOverlay flow, CityPilot context)
        {
            view = Camera.main;
            if (view == null)
                return;

            dem = grid;
            overlay = flow;
            city = context;
            inspected = -1;
            initialPosition = view.transform.position;
            initialRotation = view.transform.rotation;
            initialSize = view.orthographicSize;
            initialFarClip = view.farClipPlane;
            if (surface == null)
                surface = gameObject.AddComponent<MeshCollider>();
            surface.sharedMesh = mesh;
        }

        private void Update()
        {
            if (dem == null || view == null)
                return;

            if (Keyboard.current?.rKey.wasPressedThisFrame == true)
                ResetView();

            var mouse = Mouse.current;
            if (mouse == null)
                return;
            var pointer = mouse.position.ReadValue();
            var guiPoint = new Vector2(pointer.x, Screen.height - pointer.y);
            if (Panel.Contains(guiPoint) || overlay.CoversPanel(guiPoint) ||
                (city != null && city.CoversPanel(guiPoint)))
                return;

            var delta = mouse.delta.ReadValue();
            if (mouse.middleButton.isPressed)
                Pan(delta);
            if (mouse.rightButton.isPressed && delta != Vector2.zero)
                Orbit(delta);

            float scroll = mouse.scroll.ReadValue().y;
            if (scroll != 0)
                view.orthographicSize = Mathf.Clamp(view.orthographicSize * Mathf.Exp(-scroll * 0.002f),
                    dem.Grid.CellSizeM, initialSize * 2f);

            if (mouse.leftButton.wasPressedThisFrame)
                Inspect(pointer);
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

            GUILayout.BeginArea(Panel, GUI.skin.box);
            GUILayout.Label("Middle drag: pan · Right drag: orbit · Scroll: zoom");
            if (GUILayout.Button("Reset view (R)"))
                ResetView();

            if (inspected < 0)
                GUILayout.Label("Click the terrain to inspect a cell.");
            else
            {
                int row = inspected / dem.Grid.Cols;
                int col = inspected % dem.Grid.Cols;
                GUILayout.Label($"Cell row {row}, column {col} · Elevation {dem.ElevationM[inspected]:0.00} m");
                if (overlay.Flow is { } flow && flow.Counts[inspected] is int count)
                    GUILayout.Label($"D8 {flow.Direction[inspected]} · {count:N0} contributing cells");
                else
                    GUILayout.Label("Flow data is unavailable for this cell.");
            }
            GUILayout.EndArea();
        }

        public void Clear()
        {
            dem = null;
            view = null;
            if (surface != null)
                surface.sharedMesh = null;
        }
    }
}
