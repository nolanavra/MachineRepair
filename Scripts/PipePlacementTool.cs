using System;
using System.Collections.Generic;
using MachineRepair.Grid;
using MachineRepair.Pathing;
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

        private readonly struct DebugCandidate
        {
            public readonly Vector2Int From;
            public readonly Vector2Int To;
            public readonly bool Accepted;
            public readonly string Reason;

            public DebugCandidate(Vector2Int from, Vector2Int to, bool accepted, string reason)
            {
                From = from;
                To = to;
                Accepted = accepted;
                Reason = reason;
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
        private readonly List<LineRenderer> placedPipeRenderers = new();
        private readonly List<PlacedPipe> placedPipes = new();
        private Vector2Int? startCell;
        private InputAction pointAction;
        private Vector2 pointerScreenPosition;

        public Color PipeColor => pipeColor;
        public float PipeLineWidth => lineWidth;

        public event Action<Vector2Int> PreviewStarted;
        public event Action PreviewCancelled;
        public event Action<PlacedPipe> PipePlaced;

        [Header("Debugging")]
        [SerializeField] private bool drawPathingDebug;

        private readonly List<DebugCandidate> debugCandidates = new();

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

            if (!ApplyPipeToGrid(path, placedPipe))
            {
                Destroy(placedPipe.gameObject);
                CancelPreview();
                return;
            }
            RenderFinalPipe(path);
            placedPipes.Add(placedPipe);
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
            if (drawPathingDebug) debugCandidates.Clear();
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
                    bool allowed = IsDirectionAllowed(current.Direction, dir);
                    var candidate = current.Position + dir;
                    if (!allowed)
                    {
                        RecordDebugCandidate(current.Position, candidate, false, "Direction not allowed");
                        continue;
                    }

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

        private bool IsDirectionAllowed(Vector2Int currentDirection, Vector2Int candidateDirection)
        {
            if (currentDirection == Vector2Int.zero) return true;

            float angle = Vector2.Angle(currentDirection, candidateDirection);
            return angle <= 45f;
        }

        private void RecordDebugCandidate(Vector2Int from, Vector2Int to, bool accepted, string reason)
        {
            if (!drawPathingDebug) return;
            debugCandidates.Add(new DebugCandidate(from, to, accepted, reason));
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
            if (direction == Vector2Int.zero)
            {
                RecordDebugCandidate(current.Position, candidate, false, "No movement");
                return;
            }

            if (!PathEvaluation.IsTurnAllowed(current.Direction, direction, MinTurnAngleDegrees))
            {
                if (PathEvaluation.TryBuildWideTurn(current.Direction, direction, MinTurnAngleDegrees,
                        out var firstDiagonal, out var secondDiagonal))
                {
                    bool handled = TryEnqueueWideTurn(
                        start,
                        goal,
                        avoidExistingRuns,
                        cameFrom,
                        frontier,
                        visited,
                        current,
                        firstDiagonal,
                        secondDiagonal);

                    if (handled) return;
                }

                RecordDebugCandidate(current.Position, candidate, false, "Turn too sharp");
                return;
            }

            if (!IsCellPassable(start, goal, avoidExistingRuns, candidate, nextCell, out var reason))
            {
                RecordDebugCandidate(current.Position, candidate, false, reason);
                return;
            }

            var nextState = new PathState(candidate, direction);
            if (visited.Contains(nextState)) return;

            visited.Add(nextState);
            frontier.Enqueue(nextState);
            cameFrom[nextState] = current;
            RecordDebugCandidate(current.Position, candidate, true, "Accepted");
        }

        private bool TryEnqueueWideTurn(
            Vector2Int start,
            Vector2Int goal,
            bool avoidExistingRuns,
            Dictionary<PathState, PathState> cameFrom,
            Queue<PathState> frontier,
            HashSet<PathState> visited,
            PathState current,
            Vector2Int firstDiagonal,
            Vector2Int secondDiagonal)
        {
            var firstPosition = current.Position + firstDiagonal;
            if (!grid.InBounds(firstPosition.x, firstPosition.y)) return false;
            if (!grid.TryGetCell(firstPosition, out var firstCell)) return false;
            if (!IsCellPassable(start, goal, avoidExistingRuns, firstPosition, firstCell, out var firstReason))
            {
                RecordDebugCandidate(current.Position, firstPosition, false, firstReason);
                return false;
            }

            var firstState = new PathState(firstPosition, firstDiagonal);
            if (!visited.Contains(firstState))
            {
                visited.Add(firstState);
                frontier.Enqueue(firstState);
                cameFrom[firstState] = current;
                RecordDebugCandidate(current.Position, firstPosition, true, "Wide turn first");
            }

            var secondPosition = firstPosition + secondDiagonal;
            if (!grid.InBounds(secondPosition.x, secondPosition.y)) return true; // first step is usable; second is out of bounds
            if (!grid.TryGetCell(secondPosition, out var secondCell)) return true;
            if (!PathEvaluation.IsTurnAllowed(firstDiagonal, secondDiagonal, MinTurnAngleDegrees)) return true;
            if (!IsCellPassable(start, goal, avoidExistingRuns, secondPosition, secondCell, out var secondReason))
            {
                RecordDebugCandidate(firstPosition, secondPosition, false, secondReason);
                return true;
            }

            var secondState = new PathState(secondPosition, secondDiagonal);
            if (visited.Contains(secondState)) return true;

            visited.Add(secondState);
            frontier.Enqueue(secondState);
            cameFrom[secondState] = firstState;
            RecordDebugCandidate(firstPosition, secondPosition, true, "Wide turn second");
            return true;
        }

        private bool IsCellPassable(
            Vector2Int start,
            Vector2Int goal,
            bool avoidExistingRuns,
            Vector2Int candidate,
            cellDef candidateCell,
            out string reason)
        {
            reason = string.Empty;

            bool isGoal = candidate == goal;
            bool blockedByComponent = candidateCell.HasComponent && !isGoal && candidate != start;
            bool blockedByPlaceability = candidateCell.placeability == CellPlaceability.Blocked;
            if (blockedByComponent || blockedByPlaceability)
            {
                reason = "Cell blocked";
                return false;
            }

            bool blockedByExistingRun = avoidExistingRuns
                                         && candidate != start
                                         && candidate != goal
                                         && (candidateCell.HasPipe || candidateCell.HasWire);
            if (blockedByExistingRun)
            {
                reason = "Existing run";
                return false;
            }

            return true;
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawPathingDebug || debugCandidates.Count == 0 || grid == null) return;

            foreach (var candidate in debugCandidates)
            {
                Gizmos.color = candidate.Accepted ? Color.green : Color.red;
                var startWorld = grid.CellToWorld(candidate.From);
                var endWorld = grid.CellToWorld(candidate.To);
                startWorld.z = previewZOffset;
                endWorld.z = previewZOffset;
                Gizmos.DrawLine(startWorld, endWorld);
            }
        }

        private bool ApplyPipeToGrid(List<Vector2Int> path, PlacedPipe placedPipe)
        {
            if (path == null || placedPipe == null) return false;
            return grid.AddPipeRun(path, placedPipe);
        }

        public bool UndoLastPipe()
        {
            if (placedPipes.Count == 0) return false;

            int lastIndex = placedPipes.Count - 1;
            var pipe = placedPipes[lastIndex];
            placedPipes.RemoveAt(lastIndex);

            if (pipe != null)
            {
                grid.ClearPipeRun(pipe.occupiedCells, pipe);
                Destroy(pipe.gameObject);
            }

            if (lastIndex < placedPipeRenderers.Count)
            {
                var renderer = placedPipeRenderers[lastIndex];
                placedPipeRenderers.RemoveAt(lastIndex);
                if (renderer != null)
                {
                    Destroy(renderer.gameObject);
                }
            }

            return true;
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

            placedPipeRenderers.Add(renderer);
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
