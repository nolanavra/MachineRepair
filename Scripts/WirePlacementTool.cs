using System.Collections.Generic;
using UnityEngine;
using MachineRepair.Grid;

namespace MachineRepair
{
    /// <summary>
    /// Handles wire placement interactions during the wire connection mode.
    /// Players click two ports to connect; a preview follows the cursor after the
    /// first click and a pathfinding pass lays wire cells on completion.
    /// </summary>
    public class WirePlacementTool : MonoBehaviour
    {
        public struct WireConnectionInfo
        {
            public Vector2Int startCell;
            public Vector2Int endCell;
            public MachineComponent startComponent;
            public MachineComponent endComponent;
            public WireType wireType;
            public PlacedWire placedWire;
        }

        [Header("References")]
        [SerializeField] private GridManager grid;
        [SerializeField] private Camera cameraOverride;
        [SerializeField] private LineRenderer wirePreviewPrefab;

        [Header("Appearance")]
        [SerializeField] private Color wireColor = Color.cyan;

        [Header("Behavior")]
        [SerializeField] private WireType wireType = WireType.AC;
        [SerializeField] private float previewZOffset = -0.1f;
        [SerializeField] private float lineWidth = 0.05f;

        [Header("Simulation")]
        [SerializeField] private float defaultWireResistance = 1f;
        [SerializeField] private float maxWireCurrentBeforeDamage = 10f;
        [SerializeField] private float maxWireResistanceBeforeDamage = 5f;

        private Camera cam;
        private LineRenderer activePreview;
        private readonly List<LineRenderer> placedWires = new();
        private readonly List<WireConnectionInfo> connections = new();
        private readonly Dictionary<Vector2Int, WireConnectionInfo> connectionByCell = new();
        private Vector2Int? startCell;

        private void Awake()
        {
            cam = cameraOverride != null ? cameraOverride : Camera.main;
            if (grid == null) grid = FindFirstObjectByType<GridManager>();

            if (cam == null)
            {
                Debug.LogError("WirePlacementTool requires a Camera reference for previews.");
            }

            if (grid == null)
            {
                Debug.LogError("WirePlacementTool requires a GridManager in the scene.");
            }
        }

        private void Update()
        {
            if (startCell.HasValue)
            {
                UpdatePreviewToCursor();
            }
        }

        /// <summary>
        /// Handle a left click in wire placement mode. The first click starts a
        /// preview from the chosen port and the second click finalizes the path.
        /// </summary>
        public void HandleClick(Vector2Int cellPos)
        {
            if (grid == null || cam == null) return;
            if (!grid.InBounds(cellPos.x, cellPos.y)) return;
            if (!grid.TryGetCell(cellPos, out var cell)) return;
            if (!IsPowerPortCell(cell)) return;

            if (!startCell.HasValue)
            {
                BeginPreview(cellPos);
                return;
            }

            FinalizeWire(cellPos);
        }

        /// <summary>
        /// Cancels any in-progress preview without altering the grid.
        /// </summary>
        public void CancelPreview()
        {
            startCell = null;
            if (activePreview != null)
            {
                Destroy(activePreview.gameObject);
                activePreview = null;
            }
        }

        /// <summary>
        /// Updates the preview and future wires to use a selected color.
        /// </summary>
        public void SetWireColor(Color color)
        {
            wireColor = color;
            ApplyWireColor(activePreview);
        }

        /// <summary>
        /// Returns true when the cell represents a power connection port.
        /// </summary>
        private bool IsPowerPortCell(cellDef cell)
        {
            if (cell.placeability == CellPlaceability.Blocked) return false;
            return cell.HasComponent && cell.component.def != null && cell.component.def.type == ComponentType.ChassisPowerConnection;
        }

        private void BeginPreview(Vector2Int cellPos)
        {
            startCell = cellPos;
            EnsurePreview();

            var world = grid.CellToWorld(cellPos);
            world.z = previewZOffset;
            activePreview.positionCount = 2;
            activePreview.SetPosition(0, world);
            activePreview.SetPosition(1, world);
        }

        private void FinalizeWire(Vector2Int targetCell)
        {
            if (!startCell.HasValue) return;

            var path = FindPath(startCell.Value, targetCell);
            if (path.Count == 0)
            {
                CancelPreview();
                return;
            }

            var placedWire = CreatePlacedWire(path, targetCell);
            if (placedWire == null)
            {
                CancelPreview();
                return;
            }

            ApplyWireToGrid(path, placedWire);
            RenderFinalWire(path);
            RegisterConnection(path, targetCell, placedWire);

            startCell = null;
            activePreview = null;
        }

        private void UpdatePreviewToCursor()
        {
            if (activePreview == null || !startCell.HasValue) return;

            Vector3 screenPos = Input.mousePosition;
            Vector3 worldPos = cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, Mathf.Abs(previewZOffset)));
            worldPos.z = previewZOffset;

            activePreview.SetPosition(1, worldPos);
        }

        private void EnsurePreview()
        {
            if (activePreview != null) return;

            if (wirePreviewPrefab != null)
            {
                activePreview = Instantiate(wirePreviewPrefab, transform);
            }
            else
            {
                var go = new GameObject("WirePreview");
                go.transform.SetParent(transform, worldPositionStays: false);
                activePreview = go.AddComponent<LineRenderer>();
                activePreview.material = new Material(Shader.Find("Sprites/Default"));
                activePreview.useWorldSpace = true;
                activePreview.sortingOrder = 100;
            }

            ApplyWireColor(activePreview);
            activePreview.widthMultiplier = lineWidth;
        }

        private List<Vector2Int> FindPath(Vector2Int start, Vector2Int goal)
        {
            var result = new List<Vector2Int>();
            if (start == goal)
            {
                result.Add(start);
                return result;
            }

            var frontier = new Queue<Vector2Int>();
            var cameFrom = new Dictionary<Vector2Int, Vector2Int>();

            frontier.Enqueue(start);
            cameFrom[start] = start;

            Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

            while (frontier.Count > 0)
            {
                var current = frontier.Dequeue();
                if (current == goal) break;

                foreach (var dir in dirs)
                {
                    var next = current + dir;
                    if (!grid.InBounds(next.x, next.y)) continue;
                    if (cameFrom.ContainsKey(next)) continue;
                    if (!grid.TryGetCell(next, out var nextCell)) continue;

                    bool isGoal = next == goal;
                    bool blockedByComponent = nextCell.HasComponent && !isGoal && next != start;
                    bool blockedByPlaceability = nextCell.placeability == CellPlaceability.Blocked;
                    if (blockedByComponent || blockedByPlaceability) continue;

                    frontier.Enqueue(next);
                    cameFrom[next] = current;
                }
            }

            if (!cameFrom.ContainsKey(goal)) return result;

            var step = goal;
            while (true)
            {
                result.Insert(0, step);
                if (step == start) break;
                step = cameFrom[step];
            }

            return result;
        }

        private void ApplyWireToGrid(List<Vector2Int> path, PlacedWire placedWire)
        {
            if (path == null || placedWire == null) return;
            grid.AddWireRun(path, placedWire);
        }

        private void RenderFinalWire(List<Vector2Int> path)
        {
            if (path.Count == 0) return;

            var renderer = activePreview;
            if (renderer == null)
            {
                EnsurePreview();
                renderer = activePreview;
            }

            ApplyWireColor(renderer);
            renderer.positionCount = path.Count;
            for (int i = 0; i < path.Count; i++)
            {
                var world = grid.CellToWorld(path[i]);
                world.z = previewZOffset;
                renderer.SetPosition(i, world);
            }

            placedWires.Add(renderer);
        }

        private void ApplyWireColor(LineRenderer renderer)
        {
            if (renderer == null) return;
            renderer.startColor = wireColor;
            renderer.endColor = wireColor;
        }

        /// <summary>
        /// Retrieves the connection endpoints associated with a wire cell.
        /// </summary>
        public bool TryGetConnection(Vector2Int cell, out WireConnectionInfo info)
        {
            return connectionByCell.TryGetValue(cell, out info);
        }

        /// <summary>
        /// Returns all known wire connections.
        /// </summary>
        public IReadOnlyList<WireConnectionInfo> Connections => connections;

        private PlacedWire CreatePlacedWire(List<Vector2Int> path, Vector2Int targetCell)
        {
            if (!startCell.HasValue) return null;
            if (!grid.TryGetCell(startCell.Value, out var startCellDef)) return null;
            if (!grid.TryGetCell(targetCell, out var endCellDef)) return null;
            if (startCellDef.component == null || endCellDef.component == null) return null;

            var go = new GameObject("PlacedWire");
            go.transform.SetParent(transform, worldPositionStays: false);
            var placedWire = go.AddComponent<PlacedWire>();
            placedWire.wireType = wireType;
            placedWire.startComponent = startCellDef.component;
            placedWire.endComponent = endCellDef.component;
            placedWire.startPortCell = startCell.Value;
            placedWire.endPortCell = targetCell;
            placedWire.occupiedCells.AddRange(path);
            placedWire.resistance = defaultWireResistance;
            placedWire.EvaluateDamage(maxWireCurrentBeforeDamage, maxWireResistanceBeforeDamage);

            return placedWire;
        }

        private void RegisterConnection(List<Vector2Int> path, Vector2Int targetCell, PlacedWire placedWire)
        {
            if (!startCell.HasValue) return;
            if (!grid.TryGetCell(startCell.Value, out var startCellDef)) return;
            if (!grid.TryGetCell(targetCell, out var endCellDef)) return;
            if (startCellDef.component == null || endCellDef.component == null) return;
            if (placedWire == null) return;

            var connection = new WireConnectionInfo
            {
                startCell = startCell.Value,
                endCell = targetCell,
                startComponent = startCellDef.component,
                endComponent = endCellDef.component,
                wireType = wireType,
                placedWire = placedWire
            };

            connections.Add(connection);
            foreach (var pos in path)
            {
                connectionByCell[pos] = connection;
            }
        }
    }
}
