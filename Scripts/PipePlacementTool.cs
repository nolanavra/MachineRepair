using System;
using System.Collections.Generic;
using MachineRepair.Grid;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MachineRepair
{
    /// <summary>
    /// Handles pipe placement with diagonal and orthogonal moves while
    /// rejecting sharp turns. Mirrors the wire placement tool flow but
    /// applies a 120-degree minimum turn angle for bend validation.
    /// </summary>
    public class PipePlacementTool : MonoBehaviour
    {
        private const float MinTurnAngleDegrees = 120f;

        private struct PathState : IEquatable<PathState>
        {
            public Vector2Int Position;
            public Vector2Int Direction;

            public PathState(Vector2Int position, Vector2Int direction)
            {
                Position = position;
                Direction = direction;
            }

            public bool Equals(PathState other)
            {
                return Position == other.Position && Direction == other.Direction;
            }

            public override bool Equals(object obj)
            {
                return obj is PathState other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(Position, Direction);
            }
        }

        [Header("References")]
        [SerializeField] private GridManager grid;
        [SerializeField] private Camera cameraOverride;
        [SerializeField] private LineRenderer pipePreviewPrefab;

        [Header("Appearance")]
        [SerializeField] private Color pipeColor = new Color(0.75f, 0.55f, 1f, 1f);
        [SerializeField] private float previewZOffset = -0.1f;
        [SerializeField] private float lineWidth = 0.07f;

        [Header("Behavior")]
        [SerializeField] private PlayerInput playerInput;
        [SerializeField] private string gameplayMapName = "Gameplay";
        [SerializeField] private string pointActionName = "Point";

        [Header("Simulation")]
        [SerializeField] private PipeDef pipeDef;
        [SerializeField] private float defaultFlow = 1f;
        [SerializeField] private float defaultPressure = 1f;

        private Camera cam;
        private LineRenderer activePreview;
        private readonly List<LineRenderer> placedPipes = new();
        private Vector2Int? startCell;
        private InputAction pointAction;
        private Vector2 pointerScreenPosition;

        public Color PipeColor => pipeColor;
        public float PipeLineWidth => lineWidth;

        public event Action<Vector2Int> PreviewStarted;
        public event Action PreviewCancelled;
        public event Action<PlacedPipe> PipePlaced;

        private void Awake()
        {
            cam = cameraOverride != null ? cameraOverride : Camera.main;
            if (grid == null) grid = FindFirstObjectByType<GridManager>();

            EnsurePipeDef();
            SyncAppearanceToDef();

            if (playerInput == null) playerInput = FindFirstObjectByType<PlayerInput>();
            CacheInputActions();

            if (cam == null)
            {
                Debug.LogError("PipePlacementTool requires a Camera reference for previews.");
            }

            if (grid == null)
            {
                Debug.LogError("PipePlacementTool requires a GridManager in the scene.");
            }
        }

        private void OnEnable()
        {
            CacheInputActions();
            EnableInput();
        }

        private void OnDisable()
        {
            DisableInput();
            CancelPreview();
        }

        private void Update()
        {
            if (startCell.HasValue)
            {
                UpdatePreviewToCursor();
            }
        }

        public void HandleClick(Vector2Int cellPos)
        {
            if (grid == null || cam == null) return;
            if (!grid.InBounds(cellPos.x, cellPos.y)) return;
            if (!grid.TryGetCell(cellPos, out var cell)) return;
            if (!IsPipePortCell(cellPos, cell)) return;

            if (!startCell.HasValue)
            {
                BeginPreview(cellPos);
                return;
            }

            FinalizePipe(cellPos);
        }

        public void CancelPreview()
        {
            startCell = null;
            if (activePreview != null)
            {
                Destroy(activePreview.gameObject);
                activePreview = null;
            }

            PreviewCancelled?.Invoke();
        }

        public void SetPipeColor(Color color)
        {
            pipeColor = color;
            ApplyPipeColor(activePreview);
        }

        public void SetPipeWidth(float width)
        {
            if (width <= 0f) return;
            lineWidth = width;
            if (activePreview != null)
            {
                activePreview.widthMultiplier = lineWidth;
            }
        }

        private bool IsPipePortCell(Vector2Int cellPos, cellDef cell)
        {
            if (cell.placeability == CellPlaceability.Blocked) return false;
            if (!cell.HasComponent || cell.component == null) return false;

            var portDef = cell.component.portDef;
            if (portDef == null || portDef.ports == null || portDef.ports.Length == 0) return false;

            foreach (var port in portDef.ports)
            {
                if (port.port != PortType.Water) continue;

                var globalPortCell = cell.component.GetGlobalCell(port);
                if (globalPortCell == cellPos)
                {
                    return true;
                }
            }

            return false;
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
            PreviewStarted?.Invoke(cellPos);
        }

        private void FinalizePipe(Vector2Int targetCell)
        {
            if (!startCell.HasValue) return;

            var path = FindPath(startCell.Value, targetCell);
            if (path.Count == 0)
            {
                CancelPreview();
                return;
            }

            var placedPipe = CreatePlacedPipe(path, targetCell);
            if (placedPipe == null)
            {
                CancelPreview();
                return;
            }

            ApplyPipeToGrid(path, placedPipe);
            RenderFinalPipe(path);
            RegisterConnections(placedPipe);

            startCell = null;
            if (activePreview != null)
            {
                Destroy(activePreview.gameObject);
            }

            activePreview = null;
            PipePlaced?.Invoke(placedPipe);
        }

        private void UpdatePreviewToCursor()
        {
            if (activePreview == null || !startCell.HasValue) return;

            if (pointAction != null)
                pointerScreenPosition = pointAction.ReadValue<Vector2>();

            Vector3 worldPos = cam.ScreenToWorldPoint(new Vector3(pointerScreenPosition.x, pointerScreenPosition.y, Mathf.Abs(previewZOffset)));
            worldPos.z = previewZOffset;

            activePreview.SetPosition(1, worldPos);
        }

        private void CacheInputActions()
        {
            if (playerInput == null || playerInput.actions == null) return;

            var map = playerInput.actions.FindActionMap(gameplayMapName, throwIfNotFound: false);
            if (map == null) return;

            pointAction = map.FindAction(pointActionName, throwIfNotFound: false);
        }

        private void EnableInput()
        {
            if (pointAction != null)
            {
                pointAction.performed += OnPointPerformed;
                pointAction.canceled += OnPointPerformed;
                pointAction.Enable();
            }
        }

        private void DisableInput()
        {
            if (pointAction != null)
            {
                pointAction.performed -= OnPointPerformed;
                pointAction.canceled -= OnPointPerformed;
                pointAction.Disable();
            }
        }

        private void OnPointPerformed(InputAction.CallbackContext ctx)
        {
            pointerScreenPosition = ctx.ReadValue<Vector2>();
        }

        private void EnsurePreview()
        {
            if (activePreview != null) return;

            if (pipePreviewPrefab != null)
            {
                activePreview = Instantiate(pipePreviewPrefab, transform);
            }
            else
            {
                var go = new GameObject("PipePreview");
                go.transform.SetParent(transform, worldPositionStays: false);
                activePreview = go.AddComponent<LineRenderer>();
                activePreview.material = new Material(Shader.Find("Sprites/Default"));
                activePreview.useWorldSpace = true;
                activePreview.sortingOrder = 100;
            }

            ApplyPipeColor(activePreview);
            activePreview.widthMultiplier = lineWidth;
        }

        private List<Vector2Int> FindPath(Vector2Int start, Vector2Int goal)
        {
            var shortest = FindPathInternal(start, goal, avoidExistingRuns: false);
            if (shortest.Count == 0) return shortest;

            var avoidRuns = FindPathInternal(start, goal, avoidExistingRuns: true);
            if (avoidRuns.Count == 0) return shortest;

            float threshold = shortest.Count * 1.5f;
            return avoidRuns.Count <= threshold ? avoidRuns : shortest;
        }

        private List<Vector2Int> FindPathInternal(Vector2Int start, Vector2Int goal, bool avoidExistingRuns)
        {
            var result = new List<Vector2Int>();
            if (start == goal)
            {
                result.Add(start);
                return result;
            }

            var frontier = new Queue<PathState>();
            var cameFrom = new Dictionary<PathState, PathState>();
            var visited = new HashSet<PathState>();

            var startState = new PathState(start, Vector2Int.zero);
            frontier.Enqueue(startState);
            cameFrom[startState] = startState;
            visited.Add(startState);

            Vector2Int[] dirs =
            {
                Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right,
                new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1)
            };

            PathState goalState = default;
            bool found = false;

            while (frontier.Count > 0)
            {
                var current = frontier.Dequeue();
                if (current.Position == goal)
                {
                    goalState = current;
                    found = true;
                    break;
                }

                foreach (var dir in dirs)
                {
                    var candidate = current.Position + dir;
                    TryEnqueueNeighbor(start, goal, avoidExistingRuns, cameFrom, frontier, visited, current, candidate);
                }
            }

            if (!found) return result;

            var step = goalState;
            while (true)
            {
                result.Insert(0, step.Position);
                if (step.Position == start) break;
                step = cameFrom[step];
            }

            return result;
        }

        private void TryEnqueueNeighbor(
            Vector2Int start,
            Vector2Int goal,
            bool avoidExistingRuns,
            Dictionary<PathState, PathState> cameFrom,
            Queue<PathState> frontier,
            HashSet<PathState> visited,
            PathState current,
            Vector2Int candidate)
        {
            if (!grid.InBounds(candidate.x, candidate.y)) return;
            if (!grid.TryGetCell(candidate, out var nextCell)) return;

            var direction = candidate - current.Position;
            if (direction == Vector2Int.zero) return;

            if (current.Direction != Vector2Int.zero)
            {
                float angle = Vector2.Angle(current.Direction, direction);
                if (angle > 0f && angle < MinTurnAngleDegrees) return;
            }

            bool isGoal = candidate == goal;
            bool blockedByComponent = nextCell.HasComponent && !isGoal && candidate != start;
            bool blockedByPlaceability = nextCell.placeability == CellPlaceability.Blocked;
            if (blockedByComponent || blockedByPlaceability) return;

            if (avoidExistingRuns && candidate != start && candidate != goal)
            {
                if (nextCell.HasPipe || nextCell.HasWire) return;
            }

            var nextState = new PathState(candidate, direction);
            if (visited.Contains(nextState)) return;

            visited.Add(nextState);
            frontier.Enqueue(nextState);
            cameFrom[nextState] = current;
        }

        private void ApplyPipeToGrid(List<Vector2Int> path, PlacedPipe placedPipe)
        {
            if (path == null || placedPipe == null) return;
            grid.AddPipeRun(path, placedPipe);
        }

        private void RenderFinalPipe(List<Vector2Int> path)
        {
            if (path == null || path.Count == 0) return;

            LineRenderer renderer;
            if (pipePreviewPrefab != null)
            {
                renderer = Instantiate(pipePreviewPrefab, transform);
            }
            else
            {
                var go = new GameObject("PlacedPipeRenderer");
                go.transform.SetParent(transform, worldPositionStays: false);
                renderer = go.AddComponent<LineRenderer>();
                renderer.material = new Material(Shader.Find("Sprites/Default"));
                renderer.useWorldSpace = true;
            }

            ApplyPipeColor(renderer);
            renderer.widthMultiplier = lineWidth;
            renderer.positionCount = path.Count;
            for (int i = 0; i < path.Count; i++)
            {
                var world = grid.CellToWorld(path[i]);
                world.z = previewZOffset;
                renderer.SetPosition(i, world);
            }

            placedPipes.Add(renderer);
        }

        private void ApplyPipeColor(LineRenderer renderer)
        {
            if (renderer == null) return;
            renderer.startColor = pipeColor;
            renderer.endColor = pipeColor;
        }

        private PlacedPipe CreatePlacedPipe(List<Vector2Int> path, Vector2Int targetCell)
        {
            if (!startCell.HasValue) return null;
            if (!grid.TryGetCell(startCell.Value, out var startCellDef)) return null;
            if (!grid.TryGetCell(targetCell, out var endCellDef)) return null;
            if (startCellDef.component == null || endCellDef.component == null) return null;

            var go = new GameObject("PlacedPipe");
            go.transform.SetParent(transform, worldPositionStays: false);
            var placedPipe = go.AddComponent<PlacedPipe>();
            placedPipe.pipeDef = pipeDef;
            placedPipe.startComponent = startCellDef.component;
            placedPipe.endComponent = endCellDef.component;
            placedPipe.startPortCell = startCell.Value;
            placedPipe.endPortCell = targetCell;
            placedPipe.occupiedCells.AddRange(path);
            placedPipe.flow = defaultFlow;
            placedPipe.pressure = defaultPressure;

            return placedPipe;
        }

        private void RegisterConnections(PlacedPipe pipe)
        {
            if (pipe == null) return;
            pipe.startComponent?.RegisterConnection(PortType.Water, pipe.endComponent);
            pipe.endComponent?.RegisterConnection(PortType.Water, pipe.startComponent);
        }

        private void EnsurePipeDef()
        {
            if (pipeDef != null) return;

            pipeDef = ScriptableObject.CreateInstance<PipeDef>();
            pipeDef.displayName = "Pipe";
            pipeDef.pipeColor = pipeColor;
            pipeDef.lineWidth = lineWidth;
            pipeDef.maxFlow = defaultFlow;
            pipeDef.maxPressure = defaultPressure;
        }

        private void SyncAppearanceToDef()
        {
            if (pipeDef == null) return;
            pipeColor = pipeDef.pipeColor;
            lineWidth = pipeDef.lineWidth;
        }
    }
}
