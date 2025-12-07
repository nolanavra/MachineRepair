
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Windows;

namespace MachineRepair.EditorTools
{
    /// <summary>
    /// Port Painter for ComponentDef.
    /// Lets you place/remove ports (Water/Power/Signal In/Out) on footprint cells.
    /// Open via: Tools/Espresso/Port Painter
    /// </summary>
    public class PortPainterWindow : EditorWindow
    {
        private ThingDef def;
        private Vector2 scroll;
        private float cellPx = 22f;
        private float gridPadding = 8f;

        private PortType selectedType = PortType.Power;
        private bool selectedIsInput = true;

        private bool showList = true;

        private static readonly Color gridBg = new Color(0.16f, 0.16f, 0.16f, 1f);
        private static readonly Color gridDark = new Color(0.12f, 0.12f, 0.12f, 1f);
        private static readonly Color occFill = new Color(0.18f, 0.32f, 0.22f, 1f);
        private static readonly Color occEmpty = new Color(0.10f, 0.10f, 0.10f, 1f);

        [MenuItem("Tools/Espresso/Port Painter")]
        public static void Open()
        {
            var win = GetWindow<PortPainterWindow>("Port Painter");
            win.minSize = new Vector2(520, 300);
        }

        void OnGUI()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                EditorGUILayout.Space(2);
                def = (ThingDef)EditorGUILayout.ObjectField("Component Def", def, typeof(ThingDef), false);

                if (def == null)
                {
                    EditorGUILayout.HelpBox("Assign a ComponentDef to edit its ports.", MessageType.Info);
                    return;
                }

                // Ensure port set exists
                if (def.connectionPorts == null)
                {
                    if (GUILayout.Button("Create Port Set on this ComponentDef"))
                    {
                        CreateconnectionPortsAsset(def);
                    }
                    EditorGUILayout.Space(8);
                }

                // If still null, bail until user creates it
                if (def.connectionPorts == null)
                {
                    EditorGUILayout.HelpBox("This ComponentDef has no Port Set yet. Click the button above to create one.", MessageType.Warning);
                    return;
                }

                // Ensure footprint grid initialized
                int w = Mathf.Max(1, def.footprint.width);
                int h = Mathf.Max(1, def.footprint.height);
                if (def.footprint.occupied == null || def.footprint.occupied.Length != w * h)
                {
                    EditorGUILayout.HelpBox("Footprint not initialized. Please set it up in the Footprint Painter first.", MessageType.Warning);
                    return;
                }

                DrawToolbar();

                // Grid area
                var areaHeight = h * cellPx + gridPadding * 2f + 2f;
                var areaWidth  = w * cellPx + gridPadding * 2f + 2f;

                scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(Mathf.Min(position.height - 160f, areaHeight + 24f)));
                var rect = GUILayoutUtility.GetRect(areaWidth, areaHeight);

                // Draw backdrop
                EditorGUI.DrawRect(rect, gridDark);
                var gridRect = new Rect(rect.x + gridPadding, rect.y + gridPadding, w * cellPx, h * cellPx);
                EditorGUI.DrawRect(gridRect, gridBg);

                // Draw cells with occupancy
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int idx = y * w + x;
                    var cRect = CellRect(gridRect, w, h, x, y);
                    bool occ = def.footprint.occupied[idx];
                    EditorGUI.DrawRect(cRect, occ ? occFill : occEmpty);

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

                // Draw existing ports
                if (def.connectionPorts != null && def.connectionPorts.ports != null)
                {
                    foreach (var p in def.connectionPorts.ports)
                    {
                        DrawPortMarker(gridRect, w, h, p);
                    }
                }

                // Mouse interactions
                HandleMouse(gridRect, w, h);

                EditorGUILayout.EndScrollView();

                DrawPortList();
            }
        }

        Rect CellRect(Rect gridRect, int w, int h, int x, int y)
        {
            // flip y so 0,0 is bottom-left
            return new Rect(gridRect.x + x * cellPx, gridRect.y + (h - 1 - y) * cellPx, cellPx - 1f, cellPx - 1f);
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                selectedType = (PortType)EditorGUILayout.EnumPopup(selectedType, GUILayout.Width(120));

                // Input / Output selection box
                var ioLabels = new[] { "Input", "Output" };
                int currentIO = selectedIsInput ? 0 : 1;
                int newIO = GUILayout.Toolbar(currentIO, ioLabels, EditorStyles.toolbarButton, GUILayout.Width(140));
                selectedIsInput = (newIO == 0);

                GUILayout.FlexibleSpace();
                showList = GUILayout.Toggle(showList, "Show List", EditorStyles.toolbarButton, GUILayout.Width(80));
            }

            EditorGUILayout.Space(3);
            EditorGUILayout.HelpBox("Left-click a cell to ADD the selected port (Layer + Kind). Right-click to REMOVE matching port at that cell. Hold Ctrl + Right-click to remove ALL ports from the cell.", MessageType.None);
        }

        void HandleMouse(Rect gridRect, int w, int h)
        {
            var e = Event.current;
            if (e == null) return;

            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && gridRect.Contains(e.mousePosition))
            {
                Vector2Int cell = PixelToCell(e.mousePosition, gridRect, w, h);
                if (e.button == 0) // left add
                {
                    AddPort(cell, selectedType, selectedIsInput);
                }
                else if (e.button == 1) // right remove
                {
                    if (e.control || e.command)
                        RemoveAllPortsAt(cell);
                    else
                        RemovePort(cell, selectedType, selectedIsInput);
                }
                e.Use();
            }
        }

        Vector2Int PixelToCell(Vector2 mouse, Rect gridRect, int w, int h)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt((mouse.x - gridRect.x) / cellPx), 0, w - 1);
            int yFromTop = Mathf.Clamp(Mathf.FloorToInt((mouse.y - gridRect.y) / cellPx), 0, h - 1);
            int y = (h - 1) - yFromTop;
            return new Vector2Int(x, y);
        }

        void AddPort(Vector2Int cell,  PortType type, bool isInput)
        {
            if (def.connectionPorts == null) return;
            if (def.connectionPorts.ports == null) def.connectionPorts.ports = new PortLocal[0];

            // Avoid duplicates (same cell+layer+kind)
            for (int i = 0; i < def.connectionPorts.ports.Length; i++)
            {
                var p = def.connectionPorts.ports[i];
                if (p.cell == cell && p.port == type && p.isInput == isInput)
                    return;
            }

            Undo.RecordObject(def.connectionPorts, "Add Port");
            var list = new List<PortLocal>(def.connectionPorts.ports);
            list.Add(new PortLocal { cell = cell, port = type, isInput = isInput });
            def.connectionPorts.ports = list.ToArray();
            EditorUtility.SetDirty(def.connectionPorts);
            Repaint();
        }

        void RemovePort(Vector2Int cell, PortType type, bool isInput)
        {
            if (def.connectionPorts == null || def.connectionPorts.ports == null) return;
            var list = new List<PortLocal>(def.connectionPorts.ports);
            int removed = list.RemoveAll(p => p.cell == cell && p.port == type && p.isInput == isInput);
            if (removed > 0)
            {
                Undo.RecordObject(def.connectionPorts, "Remove Port");
                def.connectionPorts.ports = list.ToArray();
                EditorUtility.SetDirty(def.connectionPorts);
                Repaint();
            }
        }

        void RemoveAllPortsAt(Vector2Int cell)
        {
            if (def.connectionPorts == null || def.connectionPorts.ports == null) return;
            var list = new List<PortLocal>(def.connectionPorts.ports);
            int removed = list.RemoveAll(p => p.cell == cell);
            if (removed > 0)
            {
                Undo.RecordObject(def.connectionPorts, "Remove Ports");
                def.connectionPorts.ports = list.ToArray();
                EditorUtility.SetDirty(def.connectionPorts);
                Repaint();
            }
        }

        void DrawPortMarker(Rect gridRect, int w, int h, PortLocal p)
        {
            var cRect = CellRect(gridRect, w, h, p.cell.x, p.cell.y);
            Vector2 center = cRect.center;

            // Color by layer
            Color col = Color.white;
            switch (p.port)
            {
                case PortType.Water: col = new Color(0.3f, 0.7f, 1f, 1f); break;
                case PortType.Power: col = new Color(1f, 0.8f, 0.2f, 1f); break;
                case PortType.Signal: col = new Color(1f, 0f, 0f, 1f); break;
                default: col = Color.white; break;
            }

            // Outline thickness difference for In/Out
            float r = Mathf.Min(cRect.width, cRect.height) * 0.38f;
            Handles.color = col;
            Handles.DrawSolidDisc(center, Vector3.forward, r * 0.9f);
            Handles.color = (!p.isInput ? Color.black : Color.white);
            Handles.DrawWireDisc(center, Vector3.forward, r);

            // Letter
            string label = PortShortLabel(p.port, p.isInput);
            var style = new GUIStyle(EditorStyles.boldLabel);
            style.normal.textColor = Color.black;
            style.alignment = TextAnchor.MiddleCenter;
            var textRect = new Rect(center.x - 12, center.y - 8, 24, 16);
            GUI.Label(textRect, label, style);
        }

        string PortShortLabel(PortType k, bool j)
        {
            switch (k)
            {
                case PortType.Water: if (j) return "W_in"; else return "W_out";
                case PortType.Power: if(j) return "P_in"; else return "P_out";
                case PortType.Signal: if (j) return "S_in"; else return "S_out";
            }
            return "?";
        }

        void DrawPortList()
        {
            if (!showList) return;
            if (def.connectionPorts == null || def.connectionPorts.ports == null) return;

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Ports", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope("box"))
            {
                var ports = def.connectionPorts.ports;
                if (ports.Length == 0)
                {
                    EditorGUILayout.LabelField("(none)");
                    return;
                }

                for (int i = 0; i < ports.Length; i++)
                {
                    var p = ports[i];
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField($"[{i}]  Cell ({p.cell.x},{p.cell.y})  {p.isInput}  {p.port}");
                        if (GUILayout.Button("Select Cell", GUILayout.Width(90)))
                        {
                            // Scroll roughly to the cell row
                            scroll.y = Mathf.Max(0, (def.footprint.height - 1 - p.cell.y) * cellPx - position.height * 0.25f);
                        }
                        if (GUILayout.Button("Delete", GUILayout.Width(70)))
                        {
                            Undo.RecordObject(def.connectionPorts, "Delete Port");
                            var list = new List<PortLocal>(def.connectionPorts.ports);
                            list.RemoveAt(i);
                            def.connectionPorts.ports = list.ToArray();
                            EditorUtility.SetDirty(def.connectionPorts);
                            GUIUtility.ExitGUI();
                            return;
                        }
                    }
                }
            }
        }

        void CreateconnectionPortsAsset(ThingDef d)
        {
            string path = EditorUtility.SaveFilePanelInProject("Create Port Set", d.defName.Replace(" ", "_") + "_Ports", "asset", "Where to save the Port Set asset?");
            if (string.IsNullOrEmpty(path)) return;
            var ps = ScriptableObject.CreateInstance<PortDef>();
            AssetDatabase.CreateAsset(ps, path);
            Undo.RecordObject(d, "Assign Port Set");
            d.connectionPorts = ps;
            EditorUtility.SetDirty(d);
            AssetDatabase.SaveAssets();
        }
    }
}
#endif
