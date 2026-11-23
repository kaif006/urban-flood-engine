using UnityEngine;

namespace FloodTwin
{
    internal static class DemoHud
    {
        private static readonly Color Text = new Color32(235, 245, 251, 255);
        private static readonly Color Muted = new Color32(173, 195, 209, 255);
        private static readonly Color Accent = new Color32(104, 226, 220, 255);
        private static readonly Color PanelColor = new Color32(14, 27, 39, 255);

        internal static Rect DockBounds => new(0, 0, Mathf.Min(320f, Screen.width * 0.45f), Screen.height);

        private static GUIStyle panel;
        private static Texture2D controlSurface;
        private static Texture2D hoverSurface;
        private static Texture2D selectedSurface;
        private static Texture2D pressedSurface;
        internal static GUIStyle Title { get; private set; }
        internal static GUIStyle Body { get; private set; }
        internal static GUIStyle Caption { get; private set; }
        internal static GUIStyle Button { get; private set; }
        internal static GUIStyle Tab { get; private set; }

        internal static void BeginDock()
        {
            if (panel == null)
            {
                panel = new GUIStyle { padding = new RectOffset(16, 16, 16, 12) };
                Title = Label(19, Text, FontStyle.Bold);
                Body = Label(14, Text, FontStyle.Normal);
                Caption = Label(12, Muted, FontStyle.Normal);
                controlSurface = Pixel(new Color32(31, 53, 69, 255));
                hoverSurface = Pixel(new Color32(46, 76, 93, 255));
                selectedSurface = Pixel(new Color32(27, 88, 97, 255));
                pressedSurface = Pixel(new Color32(21, 66, 76, 255));
                Button = Control(13, FontStyle.Bold);
                Tab = Control(12, FontStyle.Normal);
            }

            var bounds = DockBounds;
            if (Event.current.type == EventType.Repaint)
            {
                DrawRect(bounds, PanelColor);
                DrawRect(new Rect(bounds.x, bounds.y, bounds.width, 2), Accent);
            }

            GUILayout.BeginArea(bounds, panel);
        }

        internal static void Section(string title)
        {
            GUILayout.Space(14);
            GUILayout.Label(title, Body);
            GUILayout.Space(3);
        }

        private static GUIStyle Label(int size, Color color, FontStyle weight)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                fontStyle = weight,
                wordWrap = true,
                margin = new RectOffset(0, 0, 0, 4),
                padding = new RectOffset()
            };
            style.normal.textColor = color;
            return style;
        }

        private static GUIStyle Control(int size, FontStyle weight)
        {
            var style = new GUIStyle
            {
                fontSize = size,
                fontStyle = weight,
                wordWrap = true,
                alignment = TextAnchor.MiddleCenter,
                fixedHeight = 28,
                stretchWidth = true,
                border = new RectOffset(),
                padding = new RectOffset(5, 5, 1, 1),
                margin = new RectOffset(0, 4, 2, 4)
            };
            SetState(style.normal, controlSurface, Text);
            SetState(style.hover, hoverSurface, Text);
            SetState(style.focused, hoverSurface, Accent);
            SetState(style.active, pressedSurface, Text);
            SetState(style.onNormal, selectedSurface, Accent);
            SetState(style.onHover, hoverSurface, Accent);
            SetState(style.onFocused, hoverSurface, Accent);
            SetState(style.onActive, pressedSurface, Accent);
            return style;
        }

        private static Texture2D Pixel(Color color)
        {
            var texture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixel(0, 0, color);
            texture.Apply(false, true);
            return texture;
        }

        private static void SetState(GUIStyleState state, Texture2D background, Color color)
        {
            state.background = background;
            state.textColor = color;
        }

        private static void DrawRect(Rect bounds, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(bounds, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        internal static void FlowRamp(int peak, Texture2D ramp)
        {
            var bounds = GUILayoutUtility.GetRect(0, 8, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
                GUI.DrawTexture(bounds, ramp, ScaleMode.StretchToFill);

            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label("1", Caption);
                GUILayout.FlexibleSpace();
                GUILayout.Label("Log scale", Caption);
                GUILayout.FlexibleSpace();
                GUILayout.Label(peak.ToString("N0"), Caption);
            }
        }
    }
}
