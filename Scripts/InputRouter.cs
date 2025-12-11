using System;
using System.Collections.Generic;
using MachineRepair;
using UnityEngine;
using UnityEngine.EventSystems;     // For UI-hit checks
using UnityEngine.InputSystem;      // New Input System

/// Centralizes input handling, pointer clicks, and routes to per-mode handlers via PlayerInput actions.
/// Uses GameModeManager + GridManager. Uses New Input System action maps instead of legacy polling.
namespace MachineRepair.Grid
{
    public class InputRouter : MonoBehaviour, IGameModeListener
    {
        
        public enum CellSelectionTarget
        {
            None,
            Component,
            Pipe,
            Wire
        }
        
        public struct SelectionInfo
        {
            public bool hasSelection;
            public Vector2Int cell;
            public cellDef cellData;
            public CellSelectionTarget target;
            public int wireIndex;
            public int pipeIndex;
        }

        public event Action<SelectionInfo> SelectionChanged;

        [Header("References")]
        [Tooltip("Auto-found at runtime if left unassigned.")]
        [SerializeField] private PlayerInput playerInput;
        [SerializeField] private GridManager grid;
        [SerializeField] private WirePlacementTool wireTool;
        [SerializeField] private PipePlacementTool pipeTool;
        private Camera cam;

        [Header("Behavior")]
        [Tooltip("Ignore clicks when the pointer is over UI (recommended).")]
        [SerializeField] private bool blockWhenPointerOverUI = true;

        [Header("Cell Highlighter")]
        [SerializeField] private Sprite highlightSprite;
        [Tooltip("Enable)")]
        [SerializeField] private bool highlightEnable = true;
        [Tooltip("Tint (alpha controls transparency).")]
        [SerializeField] private Color highlightTint = new Color(1f, 1f, 0f, 0.25f); // soft yellow, 25% alpha
        [SerializeField] private Color displayRequirementTint = new Color(1f, 0.65f, 0f, 0.25f);
        [Tooltip("Optional Scaling (1,1 fits a 1x1 cell).")]
        [SerializeField] private Vector2 highlightScale = new Vector2(1f, 1f);
        [SerializeField] private string highlightSortingLayer = "Default";
        [SerializeField] private int highlightSortingOrder = 1000;

        private GameObject highlightObject;
        private SpriteRenderer highlightRenderer;
        private Vector2Int highlightLastPosition;

        private int selectionCycleIndex;
        private readonly List<SelectionEntry> selectionCycleOrder = new();
        private Vector2Int selectedCell = new Vector2Int(int.MinValue, int.MinValue);
        private SelectionEntry selectedTarget = SelectionEntry.None;

        private readonly struct SelectionEntry
        {
            public readonly CellSelectionTarget target;
            public readonly int wireIndex;
            public readonly int pipeIndex;

            public SelectionEntry(CellSelectionTarget target, int wireIndex = -1, int pipeIndex = -1)
            {
                this.target = target;
                this.wireIndex = wireIndex;
                this.pipeIndex = pipeIndex;
            }

            public static SelectionEntry None => new SelectionEntry(CellSelectionTarget.None);
        }

        [Header("Input (Gameplay Map)")]
        [SerializeField] private string gameplayMapName = "Gameplay";
        [SerializeField] private string pointActionName = "Point";
        [SerializeField] private string primaryClickActionName = "PrimaryClick";
        [SerializeField] private string secondaryClickActionName = "SecondaryClick";
        [SerializeField] private string rotatePlacementActionName = "RotatePlacement";
        [SerializeField] private string deleteSelectionActionName = "DeleteSelection";

        [Header("Debugging")]
        [SerializeField] private bool logInputEvents = false;

        private InputAction pointAction;
        private InputAction primaryClickAction;
        private InputAction secondaryClickAction;
        private InputAction rotatePlacementAction;
        private InputAction deleteSelectionAction;
        private Vector2 pointerScreenPosition;
        private Vector2 lastLoggedPointerScreenPosition = new Vector2(float.NaN, float.NaN);

        public SelectionInfo CurrentSelection { get; private set; }


        private void Awake()
        {
            if (playerInput == null) playerInput = FindFirstObjectByType<PlayerInput>();
            cam = Camera.main;
            if (grid == null) grid = FindFirstObjectByType<GridManager>();
            if (wireTool == null) wireTool = FindFirstObjectByType<WirePlacementTool>();
            if (pipeTool == null) pipeTool = FindFirstObjectByType<PipePlacementTool>();
            if (wireTool == null)
            {
                Debug.LogWarning("WirePlacementTool not found; wire placement input will be ignored.");
            }
            if (pipeTool == null)
            {
                Debug.LogWarning("PipePlacementTool not found; pipe placement input will be ignored.");
            }
            SetupHighlightVisual();
            CacheInputActions();
        }

        private void OnEnable()
        {
            if (GameModeManager.Instance != null)
                GameModeManager.Instance.RegisterListener(this);

            CacheInputActions();
            EnableInputActions();
        }

        private void OnDisable()
        {
            if (GameModeManager.Instance != null)
                GameModeManager.Instance.UnregisterListener(this);

            // If we're disabled while a placement is in progress, refund the item
            // so the player doesn't lose inventory silently.
            grid?.CancelPlacement(refundItem: true, revertMode: false);

            DisableInputActions();
        }

        private void Update()
        {
            if (cam == null || grid == null) return;

            if (pointAction != null)
            {
                Vector2 newPointer = pointAction.ReadValue<Vector2>();
                if (newPointer != pointerScreenPosition)
                {
                    pointerScreenPosition = newPointer;
                    MaybeLogPointerChange(pointerScreenPosition);
                }
                else
                {
                    pointerScreenPosition = newPointer;
                }
            }

            if (blockWhenPointerOverUI && IsPointerOverUI()) return;

            if(highlightEnable)UpdateCellHighlight();
        }

       
        // -------------- Core Routing ----------------

        private void RouteLeftClick(cellDef cell, Vector2Int cellPos)
        {
            var modeManager = GameModeManager.Instance;
            if (modeManager == null)
            {
                OnLeftClick_Selection(cell, cellPos);
                return;
            }

            switch (modeManager.CurrentMode)
            {
                case GameMode.Selection:
                    OnLeftClick_Selection(cell, cellPos);
                    break;

                case GameMode.ComponentPlacement:
                    OnLeftClick_ComponentPlacement(cell, cellPos);
                    break;

                case GameMode.WirePlacement:
                    OnLeftClick_WirePlacement(cell, cellPos);
                    break;

                case GameMode.PipePlacement:
                    OnLeftClick_PipePlacement(cell, cellPos);
                    break;

                case GameMode.Simulation:
                    OnLeftClick_Simulation(cell, cellPos);
                    break;
            }

        }

        private void RouteRightClick(cellDef cell, Vector2Int cellPos)
        {
            var modeManager = GameModeManager.Instance;
            if (modeManager == null)
            {
                OnRightClick_Selection(cell, cellPos);
                return;
            }

            switch (modeManager.CurrentMode)
            {
                case GameMode.Selection:
                    OnRightClick_Selection(cell, cellPos);
                    break;

                case GameMode.ComponentPlacement:
                    OnRightClick_ComponentPlacement(cell, cellPos);
                    break;

                case GameMode.WirePlacement:
                    OnRightClick_WirePlacement(cell, cellPos);
                    break;

                case GameMode.PipePlacement:
                    OnRightClick_PipePlacement(cell, cellPos);
                    break;

                case GameMode.Simulation:
                    OnRightClick_Simulation(cell, cellPos);
                    break;
            }

        }

        
        // -------------- Per-Mode Handlers --------------
        // Put your actual calls where the comments indicate.

        #region Selection

        /// <summary>
        /// LEFT CLICK in Selection: pick/select an object in the cell.
        /// CALL: SelectionSystem.SelectAt(cellPos) or Raycast for entity under cursor.
        /// </summary>
        private void OnLeftClick_Selection(cellDef cell, Vector2Int cellPos)
        {
            if (!CellUsable(cell)) return;

            var targets = BuildSelectionTargets(cell);
            bool sameCell = cellPos == selectedCell;

            if (!sameCell) selectionCycleIndex = 0;
            else if (targets.Count > 0) selectionCycleIndex = (selectionCycleIndex + 1) % targets.Count;
            else selectionCycleIndex = 0;

            selectionCycleOrder.Clear();
            selectionCycleOrder.AddRange(targets);

            selectedCell = cellPos;
            selectedTarget = targets.Count > 0 ? targets[selectionCycleIndex] : SelectionEntry.None;

            ApplySelection(cellPos, cell, selectedTarget);
        }

        /// <summary>
        /// RIGHT CLICK in Selection: context or move command.
        /// CALL: SelectionSystem.ContextMenu(cellPos) or IssueMoveCommand().
        /// </summary>
        private void OnRightClick_Selection(cellDef cell, Vector2Int cellPos)
        {
            if (!CellUsable(cell)) return;
            ClearSelection();
        }

        #endregion

        #region Component Placement

        /// <summary>
        /// LEFT CLICK in ComponentPlacement: place a component if the cell is enabled and not occupied.
        /// CALL: BuildSystem.PlaceComponentAt(cellPos);
        /// </summary>
        private void OnLeftClick_ComponentPlacement(cellDef cell, Vector2Int cellPos)
        {
            grid?.TryPlaceCurrent(cellPos);
        }

        /// <summary>
        /// RIGHT CLICK in ComponentPlacement: rotate/cancel current ghost.
        /// CALL: BuildSystem.RotateCurrentGhost() or BuildSystem.CancelPlacement();
        /// </summary>
        private void OnRightClick_ComponentPlacement(cellDef cell, Vector2Int cellPos)
        {
            grid?.CancelPlacement(refundItem: true);
        }

        #endregion

        #region Wire Placement

        /// <summary>
        /// LEFT CLICK in WirePlacement: start/continue wire at this cell.
        /// CALL: WireTool.AddPoint(cellPos) or WireTool.StartAt(cellPos) if not started.
        /// </summary>
        private void OnLeftClick_WirePlacement(cellDef cell, Vector2Int cellPos)
        {
            if (!CellUsable(cell)) return;
            wireTool?.HandleClick(cellPos);
        }

        /// <summary>
        /// RIGHT CLICK in WirePlacement: undo last point or cancel path.
        /// CALL: WireTool.UndoLastPoint() or WireTool.CancelPath();
        /// </summary>
        private void OnRightClick_WirePlacement(cellDef cell, Vector2Int cellPos)
        {
            wireTool?.CancelPreview();
        }

        #endregion

        #region Pipe Placement

        /// <summary>
        /// LEFT CLICK in PipePlacement: start/continue pipe run.
        /// CALL: PipeTool.AddPoint(cellPos) or PipeTool.StartAt(cellPos).
        /// </summary>
        private void OnLeftClick_PipePlacement(cellDef cell, Vector2Int cellPos)
        {
            if (!CellUsable(cell)) return;
            pipeTool?.HandleClick(cellPos);
        }

        /// <summary>
        /// RIGHT CLICK in PipePlacement: undo last point or cancel run.
        /// CALL: PipeTool.UndoLastPoint() or PipeTool.CancelRun();
        /// </summary>
        private void OnRightClick_PipePlacement(cellDef cell, Vector2Int cellPos)
        {
            pipeTool?.CancelPreview();
        }

        #endregion

        #region Simulation

        /// <summary>
        /// LEFT CLICK in Simulation: probe/inspect.
        /// CALL: SimSystem.InspectCell(cellPos) or open inspector panel.
        /// </summary>
        private void OnLeftClick_Simulation(cellDef cell, Vector2Int cellPos)
        {
            // Probing usually allowed on disabled cells too; gate if you want:
            // if (!CellUsable(cell)) return;

            // TODO: Replace with your simulation inspect call.
            // SimSystem.InspectCell(cellPos);
            Debug.Log($"[Simulation] Inspect {cellPos}");
        }

        /// <summary>
        /// RIGHT CLICK in Simulation: set debug marker or clear overlays.
        /// CALL: SimSystem.ToggleMarker(cellPos) or SimOverlay.Clear();
        /// </summary>
        private void OnRightClick_Simulation(cellDef cell, Vector2Int cellPos)
        {
            // TODO: Replace with your simulation right-click action.
            // SimOverlay.ToggleMarker(cellPos);
            Debug.Log($"[Simulation] Marker/Action at {cellPos}");
        }

        #endregion

        // -------------- Helpers ----------------

        /// <summary>
        /// True if the cell exists, is enabled, and usable for build actions.
        /// Modify if you want wires/pipes allowed on occupied cells, etc.
        /// </summary>
        private static bool CellUsable(cellDef cell)
        {
            return true; // add more rules if needed
        }

        /// <summary>
        /// UI block helper for New/Old input systems.
        /// </summary>
        private static bool IsPointerOverUI()
        {
            if (EventSystem.current == null) return false;
            // Works with mouse (-1) in standalone; for advanced setups, use PointerEventData.
            return EventSystem.current.IsPointerOverGameObject();
        }

        private List<SelectionEntry> BuildSelectionTargets(cellDef cell)
        {
            var targets = new List<SelectionEntry>();

            if (cell.HasComponent) targets.Add(new SelectionEntry(CellSelectionTarget.Component));
            if (cell.HasPipe) targets.Add(new SelectionEntry(CellSelectionTarget.Pipe, pipeIndex: 0));
            if (cell.HasWire)
            {
                for (int i = 0; i < cell.Wires.Count; i++)
                {
                    targets.Add(new SelectionEntry(CellSelectionTarget.Wire, wireIndex: i));
                }
            }

            return targets;
        }

        private void ApplySelection(Vector2Int cellPos, cellDef cell, SelectionEntry target)
        {
            CurrentSelection = new SelectionInfo
            {
                hasSelection = target.target != CellSelectionTarget.None,
                cell = cellPos,
                cellData = cell,
                target = target.target,
                wireIndex = target.wireIndex,
                pipeIndex = target.pipeIndex
            };

            SelectionChanged?.Invoke(CurrentSelection);
        }

        private void ClearSelection()
        {
            selectionCycleIndex = 0;
            selectionCycleOrder.Clear();
            selectedCell = new Vector2Int(int.MinValue, int.MinValue);
            selectedTarget = SelectionEntry.None;
            CurrentSelection = new SelectionInfo { hasSelection = false };
            SelectionChanged?.Invoke(CurrentSelection);
        }

        // ----------------- Mouse helpers -----------------

        // Returns mouse position on grid
        public Vector2Int GetMousePos()
        {
            if (cam == null) return default;

            Vector3 mouseWorld = cam.ScreenToWorldPoint(new Vector3(pointerScreenPosition.x, pointerScreenPosition.y, 0f));
            mouseWorld.z = 0f;

            int x = Mathf.FloorToInt(mouseWorld.x);
            int y = Mathf.FloorToInt(mouseWorld.y);

            return new Vector2Int(x, y);
        }

        // Find the cell that the mouse is over
        public cellDef GetMouseCell()
        {
            cellDef foundCell;
            Vector2Int cellPos = GetMousePos();
            if (grid.TryGetCell(cellPos, out foundCell))
                return grid.GetCell(cellPos);
            else return default;
        }

#region Highlights
        /// Creates (once) and configures the hover visual GameObject.
        private void SetupHighlightVisual()
        {
            if (highlightObject != null && highlightRenderer != null)
            {
                // Keep color/scale/sorting in sync if tweaked at runtime
                highlightRenderer.color = highlightTint;
                highlightObject.transform.localScale = new Vector3(highlightScale.x, highlightScale.y, 1f);
                highlightRenderer.sortingLayerName = highlightSortingLayer;
                highlightRenderer.sortingOrder = highlightSortingOrder;
                return;
            }

            if (highlightObject == null)
            {
                highlightObject = new GameObject("cellHighlight");
                highlightObject.transform.SetParent(transform, worldPositionStays: true);
                highlightObject.SetActive(highlightEnable);
            }

            highlightRenderer = highlightObject.GetComponent<SpriteRenderer>();
            if (highlightRenderer == null)
                highlightRenderer = highlightObject.AddComponent<SpriteRenderer>();

            highlightRenderer.sprite = highlightSprite; // may be null; user should assign a sprite
            highlightRenderer.color = highlightTint;
            highlightObject.transform.localScale = new Vector3(highlightScale.x, highlightScale.y, 1f);
            highlightRenderer.sortingLayerName = highlightSortingLayer;
            highlightRenderer.sortingOrder = highlightSortingOrder;

            // Optional: prevent the highlight from blocking raycasts/clicks (if you use 2D colliders)
            highlightRenderer.maskInteraction = SpriteMaskInteraction.None;
        }

        private void UpdateCellHighlight()
        {
            if (!highlightEnable)
            {
                if (highlightObject != null) highlightObject.SetActive(false);
                SetFootprintHighlightsActive(false);
                return;
            }

            bool isPlacement = GameModeManager.Instance != null &&
                               GameModeManager.Instance.CurrentMode == GameMode.ComponentPlacement &&
                               grid != null &&
                               grid.IsPlacementActive;

            // Get mouse cell and validate
            Vector2Int pos = GetMousePos();

            if (isPlacement)
            {
                grid.UpdatePlacementPreview(pos);
                if (highlightObject != null) highlightObject.SetActive(false);
            }
            else
            {
                grid?.UpdatePlacementPreview(pos);
                if (!grid.InBounds(pos.x, pos.y))
                {
                    if (highlightObject != null) highlightObject.SetActive(false);
                    return;
                }

                var cell = grid.GetCell(pos);
                if (!highlightObject.activeSelf && cell.placeability != CellPlaceability.Blocked) highlightObject.SetActive(true);
                else if (cell.placeability == CellPlaceability.Blocked) highlightObject.SetActive(false);
                if (pos != highlightLastPosition)
                {

                    // Center of the cell (cell size 1)
                    Vector3 center = new Vector3(pos.x + 0.5f, pos.y + 0.5f, 0f);
                    highlightObject.transform.position = center;
                    highlightLastPosition = pos;
                }
            }
        }

#endregion

        // -------------- IGameModeListener ----------------

        public void OnEnterMode(GameMode newMode)
        {
            // Optional: per-mode cursor changes, enabling tool GameObjects, etc.
            // Example:
            // CursorManager.SetCursorForMode(newMode);
        }

        public void OnExitMode(GameMode oldMode)
        {
            // Optional: cleanup when leaving a mode (e.g., cancel wire run)
            // Example:
            // if (oldMode == GameMode.WirePlacement) WireTool.CancelIfIncomplete();
            if (oldMode == GameMode.ComponentPlacement)
            {
                grid?.CancelPlacement(refundItem: true, revertMode: false);
            }
            else if (oldMode == GameMode.WirePlacement)
            {
                wireTool?.CancelPreview();
            }
        }

        #endregion

        #region Input Actions
        private void CacheInputActions()
        {
            if (playerInput == null || playerInput.actions == null)
            {
                Debug.LogWarning("InputRouter has no PlayerInput; input will not be processed.");
                return;
            }

            var map = playerInput.actions.FindActionMap(gameplayMapName, throwIfNotFound: false);
            if (map == null)
            {
                Debug.LogWarning($"InputRouter could not find action map '{gameplayMapName}'.");
                return;
            }

            pointAction = map.FindAction(pointActionName, throwIfNotFound: false);
            primaryClickAction = map.FindAction(primaryClickActionName, throwIfNotFound: false);
            secondaryClickAction = map.FindAction(secondaryClickActionName, throwIfNotFound: false);
            rotatePlacementAction = map.FindAction(rotatePlacementActionName, throwIfNotFound: false);
            deleteSelectionAction = map.FindAction(deleteSelectionActionName, throwIfNotFound: false);
        }

        private void EnableInputActions()
        {
            if (pointAction != null)
            {
                pointAction.performed += OnPointPerformed;
                pointAction.canceled += OnPointPerformed;
                pointAction.Enable();
            }

            if (primaryClickAction != null)
            {
                primaryClickAction.performed += OnPrimaryClickPerformed;
                primaryClickAction.Enable();
            }

            if (secondaryClickAction != null)
            {
                secondaryClickAction.performed += OnSecondaryClickPerformed;
                secondaryClickAction.Enable();
            }

            if (rotatePlacementAction != null)
            {
                rotatePlacementAction.performed += OnRotatePlacementPerformed;
                rotatePlacementAction.Enable();
            }

            if (deleteSelectionAction != null)
            {
                deleteSelectionAction.performed += OnDeleteSelectionPerformed;
                deleteSelectionAction.Enable();
            }
        }

        private void DisableInputActions()
        {
            if (pointAction != null)
            {
                pointAction.performed -= OnPointPerformed;
                pointAction.canceled -= OnPointPerformed;
                pointAction.Disable();
            }

            if (primaryClickAction != null)
            {
                primaryClickAction.performed -= OnPrimaryClickPerformed;
                primaryClickAction.Disable();
            }

            if (secondaryClickAction != null)
            {
                secondaryClickAction.performed -= OnSecondaryClickPerformed;
                secondaryClickAction.Disable();
            }

            if (rotatePlacementAction != null)
            {
                rotatePlacementAction.performed -= OnRotatePlacementPerformed;
                rotatePlacementAction.Disable();
            }

            if (deleteSelectionAction != null)
            {
                deleteSelectionAction.performed -= OnDeleteSelectionPerformed;
                deleteSelectionAction.Disable();
            }
        }

        private void OnPointPerformed(InputAction.CallbackContext ctx)
        {
            pointerScreenPosition = ctx.ReadValue<Vector2>();
            MaybeLogPointerChange(pointerScreenPosition);
        }

        private bool CanProcessPointerInput()
        {
            if (cam == null || grid == null) return false;
            if (blockWhenPointerOverUI && IsPointerOverUI()) return false;
            return true;
        }

        private void OnPrimaryClickPerformed(InputAction.CallbackContext ctx)
        {
            if (!ctx.performed || !CanProcessPointerInput()) return;

            cellDef cell = GetMouseCell();
            Vector2Int pos = GetMousePos();
            if (grid.InBounds(pos.x, pos.y))
            {
                LogInputEvent($"Primary click at {pos} in mode {GameModeManager.Instance?.CurrentMode.ToString() ?? "(no mode)"}");
                RouteLeftClick(cell, pos);
            }
        }

        private void OnSecondaryClickPerformed(InputAction.CallbackContext ctx)
        {
            if (!ctx.performed || !CanProcessPointerInput()) return;

            cellDef cell = GetMouseCell();
            Vector2Int pos = GetMousePos();
            if (grid.InBounds(pos.x, pos.y))
            {
                LogInputEvent($"Secondary click at {pos} in mode {GameModeManager.Instance?.CurrentMode.ToString() ?? "(no mode)"}");
                RouteRightClick(cell, pos);
            }
        }

        private void OnRotatePlacementPerformed(InputAction.CallbackContext ctx)
        {
            if (!ctx.performed) return;
            if (GameModeManager.Instance == null || GameModeManager.Instance.CurrentMode != GameMode.ComponentPlacement) return;

            grid?.RotatePlacement();
            LogInputEvent($"Rotate placement input");
        }

        private void OnDeleteSelectionPerformed(InputAction.CallbackContext ctx)
        {
            if (!ctx.performed) return;
            if (grid != null && grid.TryDeleteSelection(CurrentSelection))
            {
                ClearSelection();
            }
            LogInputEvent("Delete selection input");
        }

        private void MaybeLogPointerChange(Vector2 screenPosition)
        {
            if (!logInputEvents) return;
            if (screenPosition == lastLoggedPointerScreenPosition) return;

            lastLoggedPointerScreenPosition = screenPosition;

            string gridPos = cam != null ? ScreenToGrid(screenPosition).ToString() : "(no camera)";
            LogInputEvent($"Pointer moved to screen {screenPosition} (grid {gridPos})");
        }

        private Vector2Int ScreenToGrid(Vector2 screenPosition)
        {
            if (cam == null) return Vector2Int.zero;

            Vector3 world = cam.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, 0f));
            world.z = 0f;
            return new Vector2Int(Mathf.FloorToInt(world.x), Mathf.FloorToInt(world.y));
        }

        private void LogInputEvent(string message)
        {
            if (!logInputEvents) return;
            Debug.Log($"[InputRouter] {message}");
        }
        #endregion
    }
}

