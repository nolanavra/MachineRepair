
#if UNITY_EDITOR
using MachineRepair;
using UnityEditor;
using UnityEngine;

namespace MachineRepair.EditorTools
{
    /// <summary>
    /// Footprint Painter for ThingDef. Paint occupied cells and set origin visually.
    /// Open via: Tools/Espresso/Footprint Painter
    /// </summary>
    public class FootprintPainterWindow : EditorWindow
    {
        private ThingDef def;
        private Vector2 scroll;
        private float cellPx = 22f;         // pixel size of a cell in the painter
        private float gridPadding = 8f;
        private enum ToolMode { Paint, Erase, DisplayPaint, DisplayErase, Origin }
        private ToolMode mode = ToolMode.Paint;

        private bool dragActive = false;
        private bool dragState = true;  // paint or erase during drag
        private bool dragAffectsDisplay = false;

        [MenuItem("Tools/Espresso/Footprint Painter")]
        public static void Open()
        {
            var win = GetWindow<FootprintPainterWindow>("Footprint Painter");
            win.minSize = new Vector2(420, 260);
        }

        void OnGUI()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                EditorGUILayout.Space(2);
                def = (ThingDef)EditorGUILayout.ObjectField("Component Def", def, typeof(ThingDef), false);

                if (def == null)
                {
                    EditorGUILayout.HelpBox("Assign a ComponentDef to start painting.", MessageType.Info);
                    return;
                }

                // Ensure array initialized
                int w = Mathf.Max(1, def.footprint.width);
                int h = Mathf.Max(1, def.footprint.height);
                bool needsSizeFix = def.footprint.width != w || def.footprint.height != h;
                bool needsOccupied = def.footprint.occupied == null || def.footprint.occupied.Length != w * h;
                bool needsDisplay = def.footprint.display == null || def.footprint.display.Length != w * h;
                if (needsSizeFix || needsOccupied || needsDisplay)
                {
                    Undo.RecordObject(def, "Init Footprint");
                    def.footprint.width = w;
                    def.footprint.height = h;
                    if (needsOccupied) def.footprint.occupied = new bool[w * h];
                    if (needsDisplay) def.footprint.display = new bool[w * h];
                    EditorUtility.SetDirty(def);
                }

                DrawToolbar(ref w, ref h);

                // Grid area
                var areaHeight = h * cellPx + gridPadding * 2f + 2f;
                var areaWidth  = w * cellPx + gridPadding * 2f + 2f;

                scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(Mathf.Min(position.height - 120f, areaHeight + 24f)));
                var rect = GUILayoutUtility.GetRect(areaWidth, areaHeight);

                // Draw backdrop
                EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.12f, 1f));
                var gridRect = new Rect(rect.x + gridPadding, rect.y + gridPadding, w * cellPx, h * cellPx);
                EditorGUI.DrawRect(gridRect, new Color(0.16f, 0.16f, 0.16f, 1f));

                // Draw cells
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int idx = y * w + x;
                    var cRect = new Rect(gridRect.x + x * cellPx, gridRect.y + (h-1-y) * cellPx, cellPx - 1f, cellPx - 1f); // flip y so 0,0 is bottom-left

                    // Fill
                    Color fill = def.footprint.occupied[idx] ? new Color(0.25f, 0.65f, 0.35f, 1f) : new Color(0.10f, 0.10f, 0.10f, 1f);
                    EditorGUI.DrawRect(cRect, fill);

                    if (def.footprint.display != null && def.footprint.display[idx])
                    {
                        var displayColor = new Color(0.30f, 0.55f, 0.95f, 0.35f);
                        EditorGUI.DrawRect(cRect, displayColor);
                    }

                    // Origin marker
                    if (def.footprint.origin.x == x && def.footprint.origin.y == y)
                    {
                        var oRect = new Rect(cRect.x + 4, cRect.y + 4, cRect.width - 8, cRect.height - 8);
                        EditorGUI.DrawRect(oRect, new Color(0.9f, 0.8f, 0.25f, 1f));
                    }
                }

                // Grid lines
                Handles.color = new Color(1f, 1f, 1f, 0.12f);
                for (int gx = 0; gx <= w; gx++)
                {
                    float x = gridRect.x + gx * cellPx;
                    Handles.DrawLine(new Vector3(x, gridRect.y), new Vector3(x, gridRect.yMax));
                }
                for (int gy = 0; gy <= h; gy++)
                {
                    float yline = gridRect.y + gy * cellPx;
                    Handles.DrawLine(new Vector3(gridRect.x, yline), new Vector3(gridRect.xMax, yline));
                }

                // Mouse painting
                HandleMouse(gridRect, w, h);

                EditorGUILayout.EndScrollView();

                // Save tip
                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox("Tip: Origin (yellow) is the pivot used for placement & rotation. Display cells (blue overlay) mark front-panel-visible tiles. 0,0 is bottom-left.", MessageType.None);
            }
        }

        void DrawToolbar(ref int w, ref int h)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                // Size controls
                EditorGUILayout.LabelField("Width", GUILayout.Width(40));
                int newW = EditorGUILayout.IntField(def.footprint.width, GUILayout.Width(40));
                EditorGUILayout.LabelField("Height", GUILayout.Width(45));
                int newH = EditorGUILayout.IntField(def.footprint.height, GUILayout.Width(40));
                if (newW != def.footprint.width || newH != def.footprint.height)
                {
                    newW = Mathf.Max(1, newW);
                    newH = Mathf.Max(1, newH);
                    if (GUILayout.Button("Apply Size", EditorStyles.toolbarButton, GUILayout.Width(80)))
                    {
                        ResizeFootprint(newW, newH);
                    }
                }

                GUILayout.FlexibleSpace();

                // Tool mode
                mode = (ToolMode)EditorGUILayout.EnumPopup(mode, GUILayout.Width(110));

                // Cell size (zoom)
                EditorGUILayout.LabelField("Zoom", GUILayout.Width(40));
                cellPx = GUILayout.HorizontalSlider(cellPx, 10f, 40f, GUILayout.Width(120));
                cellPx = Mathf.Round(cellPx);

                // Commands
                if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(50)))
                {
                    Undo.RecordObject(def, "Clear Footprint");
                    for (int i = 0; i < def.footprint.occupied.Length; i++) def.footprint.occupied[i] = false;
                    EditorUtility.SetDirty(def);
                }
                if (GUILayout.Button("Fill", EditorStyles.toolbarButton, GUILayout.Width(40)))
                {
                    Undo.RecordObject(def, "Fill Footprint");
                    for (int i = 0; i < def.footprint.occupied.Length; i++) def.footprint.occupied[i] = true;
                    EditorUtility.SetDirty(def);
                }
                if (GUILayout.Button("Invert", EditorStyles.toolbarButton, GUILayout.Width(55)))
                {
                    Undo.RecordObject(def, "Invert Footprint");
                    for (int i = 0; i < def.footprint.occupied.Length; i++) def.footprint.occupied[i] = !def.footprint.occupied[i];
                    EditorUtility.SetDirty(def);
                }
            }
        }

        void ResizeFootprint(int newW, int newH)
        {
            Undo.RecordObject(def, "Resize Footprint");
            bool[] old = def.footprint.occupied ?? new bool[0];
            bool[] oldDisplay = def.footprint.display ?? new bool[0];
            int oldW = def.footprint.width;
            int oldH = def.footprint.height;

            var next = new bool[newW * newH];
            var nextDisplay = new bool[newW * newH];
            for (int y = 0; y < Mathf.Min(oldH, newH); y++)
            for (int x = 0; x < Mathf.Min(oldW, newW); x++)
            {
                int oldIdx = y * oldW + x;
                int newIdx = y * newW + x;
                next[newIdx] = oldIdx < old.Length && old[oldIdx];
                nextDisplay[newIdx] = oldIdx < oldDisplay.Length && oldDisplay[oldIdx];
            }

            def.footprint.width = newW;
            def.footprint.height = newH;
            def.footprint.occupied = next;
            def.footprint.display = nextDisplay;

            // Clamp origin
            def.footprint.origin.x = Mathf.Clamp(def.footprint.origin.x, 0, newW - 1);
            def.footprint.origin.y = Mathf.Clamp(def.footprint.origin.y, 0, newH - 1);

            EditorUtility.SetDirty(def);
        }

        void HandleMouse(Rect gridRect, int w, int h)
        {
            var e = Event.current;
            if (e == null) return;

            if (e.type == EventType.MouseDown && gridRect.Contains(e.mousePosition))
            {
                GUI.FocusControl(null);
                e.Use();
                dragActive = true;

                var cell = PixelToCell(e.mousePosition, gridRect, w, h, out int idx);
                if (mode == ToolMode.Origin)
                {
                    SetOrigin(cell);
                }
                else
                {
                    bool state = (mode == ToolMode.Paint || mode == ToolMode.DisplayPaint);
                    dragState = state;
                    dragAffectsDisplay = (mode == ToolMode.DisplayPaint || mode == ToolMode.DisplayErase);

                    if (dragAffectsDisplay)
                        PaintDisplayCell(idx, state);
                    else
                        PaintCell(idx, state);
                }
            }
            else if (e.type == EventType.MouseDrag && dragActive)
            {
                if (gridRect.Contains(e.mousePosition))
                {
                    var cell = PixelToCell(e.mousePosition, gridRect, w, h, out int idx);
                    if (mode == ToolMode.Origin)
                    {
                        SetOrigin(cell);
                    }
                    else
                    {
                        if (dragAffectsDisplay)
                            PaintDisplayCell(idx, dragState);
                        else
                            PaintCell(idx, dragState);
                    }
                }
                e.Use();
            }
            else if (e.type == EventType.MouseUp)
            {
                dragActive = false;
                dragAffectsDisplay = false;
            }
        }

        Vector2Int PixelToCell(Vector2 mouse, Rect gridRect, int w, int h, out int index)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt((mouse.x - gridRect.x) / cellPx), 0, w - 1);
            // Flip Y so 0 is bottom
            int yFromTop = Mathf.Clamp(Mathf.FloorToInt((mouse.y - gridRect.y) / cellPx), 0, h - 1);
            int y = (h - 1) - yFromTop;

            index = y * w + x;
            return new Vector2Int(x, y);
        }

        void PaintCell(int idx, bool state)
        {
            Undo.RecordObject(def, "Paint Footprint");
            def.footprint.occupied[idx] = state;
            EditorUtility.SetDirty(def);
            Repaint();
        }

        void PaintDisplayCell(int idx, bool state)
        {
            Undo.RecordObject(def, "Paint Display Footprint");
            def.footprint.display[idx] = state;
            EditorUtility.SetDirty(def);
            Repaint();
        }

        void SetOrigin(Vector2Int cell)
        {
            Undo.RecordObject(def, "Set Origin");
            def.footprint.origin = cell;
            EditorUtility.SetDirty(def);
            Repaint();
        }
    }
}
#endif
