using System;
using System.Collections.Generic;
using MachineRepair.Grid;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MachineRepair
{
    /// <summary>
    /// Handles pipe placement with diagonal and orthogonal moves.
    /// Mirrors the wire placement tool flow without enforcing bend limits.
    /// </summary>
    public class PipePlacementTool : MonoBehaviour
    {
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
        [SerializeField, Min(0f)] private float cornerRadius = 0.25f;
        [SerializeField, Min(0)] private int samplesPerCorner = 2;
        [SerializeField, Min(0)] private int lineCornerVertices = 1;
        [SerializeField, Min(0)] private int lineCapVertices = 1;

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
            ConfigureLineRenderer(activePreview);
        }

        private bool IsPipePortCell(Vector2Int cellPos, cellDef cell)
        {
            if (cell.placeability == CellPlaceability.Blocked) return false;
            if (!cell.HasComponent || cell.component == null) return false;

            var portDef = cell.component.portDef;
            if (portDef == null || portDef.ports == null || portDef.ports.Length == 0) return false;

            foreach (var port in portDef.ports)
            {
                if (port.portType != PortType.Water) continue;

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
            RenderFinalPipe(path, placedPipe);
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

            List<Vector2Int> path = null;
            var start = startCell.Value;
            if (grid != null)
            {
                var targetCell = grid.WorldToCell(worldPos);
                if (grid.InBounds(targetCell.x, targetCell.y) && grid.TryGetCell(targetCell, out _))
                {
                    path = FindPath(start, targetCell);
                    if (path.Count == 0)
                    {
                        path = null;
                    }
                }
            }

            if (path != null)
            {
                var curved = GenerateCurvedPath(path, cornerRadius, samplesPerCorner);
                ConfigureLineRenderer(activePreview);
                SetRendererPositions(activePreview, curved);
            }
            else
            {
                var startWorld = grid.CellToWorld(start);
                startWorld.z = previewZOffset;
                ConfigureLineRenderer(activePreview);
                activePreview.positionCount = 2;
                activePreview.SetPosition(0, startWorld);
                activePreview.SetPosition(1, worldPos);
            }
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
            ConfigureLineRenderer(activePreview);
        }

        private List<Vector2Int> FindPath(Vector2Int start, Vector2Int goal)
        {
            // First pass forbids stepping through components to prefer clean tiles. If that fails,
            // fall back to a secondary search that allows component cells while still respecting
            // blocked tiles and existing run avoidance rules.
            var pathWithoutComponents = FindPathWithRunPreference(start, goal, allowComponents: false);
            if (pathWithoutComponents.Count > 0) return pathWithoutComponents;

            return FindPathWithRunPreference(start, goal, allowComponents: true);
        }

        private List<Vector2Int> FindPathWithRunPreference(Vector2Int start, Vector2Int goal, bool allowComponents)
        {
            var shortest = FindPathInternal(start, goal, allowComponents, avoidExistingRuns: false);
            if (shortest.Count == 0) return shortest;

            var avoidRuns = FindPathInternal(start, goal, allowComponents, avoidExistingRuns: true);
            if (avoidRuns.Count == 0) return shortest;

            float threshold = shortest.Count * 1.5f;
            return avoidRuns.Count <= threshold ? avoidRuns : shortest;
        }

        private List<Vector2Int> FindPathInternal(Vector2Int start, Vector2Int goal, bool allowComponents, bool avoidExistingRuns)
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

            Vector2Int[] dirs = NeighborOffsets;

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

                    TryEnqueueNeighbor(start, goal, allowComponents, avoidExistingRuns, cameFrom, frontier, visited, current, candidate);
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

        private static readonly Vector2Int[] NeighborOffsets =
        {
            Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right,
            new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1)
        };

        private bool IsDirectionAllowed(Vector2Int currentDirection, Vector2Int candidateDirection)
        {
            if (candidateDirection == Vector2Int.zero) return false;
            if (currentDirection == Vector2Int.zero) return true;
            if (candidateDirection == currentDirection) return true;

            var relativeToStraight = candidateDirection - currentDirection;
            for (int i = 0; i < NeighborOffsets.Length; i++)
            {
                if (relativeToStraight == NeighborOffsets[i])
                {
                    return true;
                }
            }

            return false;
        }

        private void RecordDebugCandidate(Vector2Int from, Vector2Int to, bool accepted, string reason)
        {
            if (!drawPathingDebug) return;
            debugCandidates.Add(new DebugCandidate(from, to, accepted, reason));
        }

        private void TryEnqueueNeighbor(
            Vector2Int start,
            Vector2Int goal,
            bool allowComponents,
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

            if (!IsCellPassable(start, goal, allowComponents, avoidExistingRuns, candidate, nextCell, out var reason))
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

        private bool IsCellPassable(
            Vector2Int start,
            Vector2Int goal,
            bool allowComponents,
            bool avoidExistingRuns,
            Vector2Int candidate,
            cellDef candidateCell,
            out string reason)
        {
            reason = string.Empty;

            bool isGoal = candidate == goal;
            bool blockedByComponent = !allowComponents && candidateCell.HasComponent && !isGoal && candidate != start;
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

            if (pipe != null && pipe.lineRenderer != null)
            {
                placedPipeRenderers.Remove(pipe.lineRenderer);
                Destroy(pipe.lineRenderer.gameObject);
            }

            return true;
        }

        private void RenderFinalPipe(List<Vector2Int> path, PlacedPipe placedPipe)
        {
            if (path == null || path.Count == 0 || placedPipe == null) return;

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
            ConfigureLineRenderer(renderer);
            var curved = GenerateCurvedPath(path, cornerRadius, samplesPerCorner);
            SetRendererPositions(renderer, curved);

            placedPipeRenderers.Add(renderer);
            renderer.transform.SetParent(placedPipe.transform, worldPositionStays: false);
            placedPipe.lineRenderer = renderer;
        }

        private void ConfigureLineRenderer(LineRenderer renderer)
        {
            if (renderer == null) return;

            renderer.widthMultiplier = lineWidth;
            renderer.numCornerVertices = lineCornerVertices;
            renderer.numCapVertices = lineCapVertices;
        }

        private List<Vector3> GenerateCurvedPath(List<Vector2Int> path, float radius, int samples)
        {
            var points = new List<Vector3>();
            if (grid == null || path == null || path.Count == 0)
            {
                return points;
            }

            if (path.Count == 1)
            {
                points.Add(ToWorld(path[0]));
                return points;
            }

            Vector3 ToWorld(Vector2Int cell)
            {
                var w = grid.CellToWorld(cell);
                w.z = previewZOffset;
                return w;
            }

            points.Add(ToWorld(path[0]));

            for (int i = 1; i < path.Count - 1; i++)
            {
                var prev = path[i - 1];
                var current = path[i];
                var next = path[i + 1];

                var dirPrev = current - prev;
                var dirNext = next - current;

                if (dirPrev == Vector2Int.zero || dirNext == Vector2Int.zero || dirPrev == dirNext)
                {
                    points.Add(ToWorld(current));
                    continue;
                }

                Vector2 dirPrevNorm = ((Vector2)dirPrev).normalized;
                Vector2 dirNextNorm = ((Vector2)dirNext).normalized;

                float offset = Mathf.Min(radius, dirPrev.magnitude * 0.5f, dirNext.magnitude * 0.5f);
                if (offset <= 0f)
                {
                    points.Add(ToWorld(current));
                    continue;
                }

                var cornerWorld = ToWorld(current);
                var entry = cornerWorld - new Vector3(dirPrevNorm.x, dirPrevNorm.y, 0f) * offset;
                var exit = cornerWorld + new Vector3(dirNextNorm.x, dirNextNorm.y, 0f) * offset;

                points.Add(entry);

                for (int s = 1; s <= samples; s++)
                {
                    float t = s / (samples + 1f);
                    points.Add(EvaluateQuadraticBezier(entry, cornerWorld, exit, t));
                }

                points.Add(exit);
            }

            points.Add(ToWorld(path[^1]));
            return points;
        }

        private static Vector3 EvaluateQuadraticBezier(Vector3 start, Vector3 control, Vector3 end, float t)
        {
            float oneMinusT = 1f - t;
            return oneMinusT * oneMinusT * start + 2f * oneMinusT * t * control + t * t * end;
        }

        private void SetRendererPositions(LineRenderer renderer, List<Vector3> points)
        {
            if (renderer == null || points == null || points.Count == 0) return;

            renderer.positionCount = points.Count;
            renderer.SetPositions(points.ToArray());
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
            pipeDef.cornerRadius = cornerRadius;
            pipeDef.samplesPerCorner = samplesPerCorner;
            pipeDef.lineCornerVertices = lineCornerVertices;
            pipeDef.lineCapVertices = lineCapVertices;
            pipeDef.maxFlow = defaultFlow;
            pipeDef.maxPressure = defaultPressure;
        }

        private void SyncAppearanceToDef()
        {
            if (pipeDef == null) return;
            pipeColor = pipeDef.pipeColor;
            lineWidth = pipeDef.lineWidth;
            cornerRadius = pipeDef.cornerRadius;
            samplesPerCorner = pipeDef.samplesPerCorner;
            lineCornerVertices = pipeDef.lineCornerVertices;
            lineCapVertices = pipeDef.lineCapVertices;
        }
    }
}
