using System;
using System.Collections.Generic;
using MachineRepair;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.EventSystems;     // For UI-hit checks

/// Centralizes input handling, keys, mouseclick  (left/right) and routes to per-mode handlers.
/// Uses GameModeManager + GridManager. Uses legacy Input API.
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
        }

        public event Action<SelectionInfo> SelectionChanged;
        
        [Header("References")]
        [Tooltip("Auto-found at runtime if left unassigned.")]
        [SerializeField] private GridManager grid;
        [SerializeField] private Inventory inventory;
        [SerializeField] private GameObject currentComponentPrefab;
        [SerializeField] private WirePlacementTool wireTool;
        private Camera cam;

        [Header("Placement State")]
        [SerializeField] private ThingDef currentPlacementDef;
        [SerializeField] private int currentRotation;
        private string currentPlacementItemId;

        [Header("Behavior")]
        [Tooltip("Ignore clicks when the pointer is over UI (recommended).")]
        [SerializeField] private bool blockWhenPointerOverUI = true;

        [Header("Cell Highlighter")]
        [SerializeField] private Sprite highlightSprite;
        [Tooltip("Enable)")]
        [SerializeField] private bool highlightEnable = true;
        [Tooltip("Tint (alpha controls transparency).")]
        [SerializeField] private Color highlightTint = new Color(1f, 1f, 0f, 0.25f); // soft yellow, 25% alpha
        [Tooltip("Optional Scaling (1,1 fits a 1x1 cell).")]
        [SerializeField] private Vector2 highlightScale = new Vector2(1f, 1f);
        [SerializeField] private string highlightSortingLayer = "Default";
        [SerializeField] private int highlightSortingOrder = 1000;

        private GameObject highlightObject;
        private SpriteRenderer highlightRenderer;
        private GameObject placementPreviewObject;
        private SpriteRenderer placementPreviewRenderer;
        private Vector2Int highlightLastPosition;
        private readonly List<SpriteRenderer> footprintHighlights = new();

        private int selectionCycleIndex;
        private readonly List<CellSelectionTarget> selectionCycleOrder = new();
        private Vector2Int selectedCell = new Vector2Int(int.MinValue, int.MinValue);
        private CellSelectionTarget selectedTarget = CellSelectionTarget.None;

        public SelectionInfo CurrentSelection { get; private set; }
        

        private void Awake()
        {
            cam = Camera.main;
            if (grid == null) grid = FindFirstObjectByType<GridManager>();
            if (inventory == null) inventory = FindFirstObjectByType<Inventory>();
            if (wireTool == null) wireTool = FindFirstObjectByType<WirePlacementTool>();
            if (wireTool == null)
            {
                Debug.LogWarning("WirePlacementTool not found; wire placement input will be ignored.");
            }
            SetupHighlightVisual();
        }

        private void OnEnable()
        {
            if (GameModeManager.Instance != null)
                GameModeManager.Instance.RegisterListener(this);
        }

        private void OnDisable()
        {
            if (GameModeManager.Instance != null)
                GameModeManager.Instance.UnregisterListener(this);

            // If we're disabled while a placement is in progress, refund the item
            // so the player doesn't lose inventory silently.
            RefundPendingPlacement();
        }

        private void Update()
        {
            if (!Input.mousePresent || cam == null || grid == null) return;

            if (blockWhenPointerOverUI && IsPointerOverUI()) return;

            HandlePlacementHotkeys();

            if(highlightEnable)UpdateCellHighlight();

            // LEFT CLICK
            if (Input.GetMouseButtonDown(0))
            {
                cellDef cell = GetMouseCell();
                Vector2Int pos = GetMousePos();
                if (grid.InBounds(pos.x, pos.y))
                    RouteLeftClick(cell, pos);
            }

            // RIGHT CLICK
            if (Input.GetMouseButtonDown(1))
            {
                cellDef cell = GetMouseCell();
                Vector2Int pos = GetMousePos();
                if (grid.InBounds(pos.x, pos.y))
                    RouteRightClick(cell, pos);
            }
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
            selectedTarget = targets.Count > 0 ? targets[selectionCycleIndex] : CellSelectionTarget.None;

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
            if (currentPlacementDef == null) return;

            var footprintCells = GetFootprintCells(cellPos, currentPlacementDef, currentRotation);
            if (!IsFootprintValid(footprintCells)) return;

            MachineComponent machine = CreatePlacedComponent(cellPos, footprintCells);
            if (machine == null) return;

            for (int i = 0; i < footprintCells.Count; i++)
            {
                var target = footprintCells[i];
                grid.TryPlaceComponent(target, machine);
            }

            ExitPlacementMode();
        }

        /// <summary>
        /// RIGHT CLICK in ComponentPlacement: rotate/cancel current ghost.
        /// CALL: BuildSystem.RotateCurrentGhost() or BuildSystem.CancelPlacement();
        /// </summary>
        private void OnRightClick_ComponentPlacement(cellDef cell, Vector2Int cellPos)
        {
            CancelPlacement(returnItemToInventory: true);
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

            // TODO: Replace with your pipe placement logic.
            // if (!PipeTool.HasActiveRun) PipeTool.StartAt(cellPos); else PipeTool.AddPoint(cellPos);
            Debug.Log($"[PipePlacement] Point at {cellPos}");
        }

        /// <summary>
        /// RIGHT CLICK in PipePlacement: undo last point or cancel run.
        /// CALL: PipeTool.UndoLastPoint() or PipeTool.CancelRun();
        /// </summary>
        private void OnRightClick_PipePlacement(cellDef cell, Vector2Int cellPos)
        {
            // TODO: Replace with your pipe cancel/undo logic.
            // PipeTool.UndoLastPoint();
            Debug.Log($"[PipePlacement] Undo/Cancel at {cellPos}");
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

        private List<CellSelectionTarget> BuildSelectionTargets(cellDef cell)
        {
            var targets = new List<CellSelectionTarget>();

            if (cell.HasComponent) targets.Add(CellSelectionTarget.Component);
            if (cell.HasPipe) targets.Add(CellSelectionTarget.Pipe);
            if (cell.HasWire) targets.Add(CellSelectionTarget.Wire);

            return targets;
        }

        private void ApplySelection(Vector2Int cellPos, cellDef cell, CellSelectionTarget target)
        {
            CurrentSelection = new SelectionInfo
            {
                hasSelection = target != CellSelectionTarget.None,
                cell = cellPos,
                cellData = cell,
                target = target
            };

            SelectionChanged?.Invoke(CurrentSelection);
        }

        private void ClearSelection()
        {
            selectionCycleIndex = 0;
            selectionCycleOrder.Clear();
            selectedCell = new Vector2Int(int.MinValue, int.MinValue);
            selectedTarget = CellSelectionTarget.None;
            CurrentSelection = new SelectionInfo { hasSelection = false };
            SelectionChanged?.Invoke(CurrentSelection);
        }

        // ----------------- Mouse helpers -----------------

        // Returns mouse position on grid
        public Vector2Int GetMousePos()
        {
            Camera cam = Camera.main;
            if (cam == null) return default;

            Vector3 screenPos = Input.mousePosition;
            Vector3 mouseWorld = cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 0f));
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
                               currentPlacementDef != null;

            // Get mouse cell and validate
            Vector2Int pos = GetMousePos();

            if (isPlacement)
            {
                var cells = GetFootprintCells(pos, currentPlacementDef, currentRotation);
                bool valid = IsFootprintValid(cells);
                SetFootprintHighlights(cells, valid);
                if (highlightObject != null) highlightObject.SetActive(false);
            }
            else
            {
                SetFootprintHighlightsActive(false);
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
                RefundPendingPlacement();
                ClearPlacementVisuals();
            }
            else if (oldMode == GameMode.WirePlacement)
            {
                wireTool?.CancelPreview();
            }
        }

        #region Component Placement Helpers
        public bool BeginComponentPlacement(string itemId, bool removeFromInventory = true)
        {
            if (string.IsNullOrEmpty(itemId) || inventory == null) return false;

            ThingDef def = inventory.GetDef(itemId);
            if (def == null) return false;

            if (removeFromInventory && !inventory.RemoveItem(itemId, 1)) return false;

            currentPlacementDef = def;
            currentPlacementItemId = itemId;
            currentRotation = 0;
            GameModeManager.Instance?.SetMode(GameMode.ComponentPlacement);
            return true;
        }

        private void HandlePlacementHotkeys()
        {
            if (GameModeManager.Instance == null) return;
            if (GameModeManager.Instance.CurrentMode != GameMode.ComponentPlacement) return;
            if (currentPlacementDef == null) return;

            if (Input.GetKeyDown(KeyCode.R))
            {
                currentRotation = (currentRotation + 1) % 4;
            }
        }

        private List<Vector2Int> GetFootprintCells(Vector2Int originCell, ThingDef def, int rotation)
        {
            var cells = new List<Vector2Int>();
            var footprint = def.footprint;
            for (int y = 0; y < footprint.height; y++)
            {
                for (int x = 0; x < footprint.width; x++)
                {
                    if (!footprint.occupied[y * footprint.width + x]) continue;

                    Vector2Int local = new Vector2Int(x - footprint.origin.x, y - footprint.origin.y);
                    Vector2Int rotated = rotation switch
                    {
                        1 => new Vector2Int(local.y, -local.x),
                        2 => new Vector2Int(-local.x, -local.y),
                        3 => new Vector2Int(-local.y, local.x),
                        _ => local
                    };

                    cells.Add(originCell + rotated);
                }
            }
            return cells;
        }

        private Vector3 GetFootprintCenterWorld(IReadOnlyList<Vector2Int> cells)
        {
            if (grid == null || cells == null || cells.Count == 0) return Vector3.zero;

            Vector3 sum = Vector3.zero;
            for (int i = 0; i < cells.Count; i++)
            {
                sum += grid.CellToWorld(cells[i]);
            }

            return sum / cells.Count;
        }

        private bool IsFootprintValid(List<Vector2Int> cells)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                Vector2Int c = cells[i];
                if (!grid.InBounds(c.x, c.y)) return false;
                var cell = grid.GetCell(c);
                if (cell.placeability == CellPlaceability.Blocked) return false;
                if (cell.HasComponent) return false;
            }
            return true;
        }

        private MachineComponent CreatePlacedComponent(Vector2Int anchorCell, IReadOnlyList<Vector2Int> footprintCells)
        {
            if (currentPlacementDef == null || grid == null) return null;

            GameObject instance = currentComponentPrefab != null
                ? Instantiate(currentComponentPrefab)
                : new GameObject(currentPlacementDef.displayName ?? currentPlacementDef.defName ?? "MachineComponent");

            instance.name = currentPlacementDef.displayName ?? currentPlacementDef.defName ?? instance.name;

            var machine = instance.GetComponent<MachineComponent>();
            if (machine == null)
                machine = instance.AddComponent<MachineComponent>();

            machine.def = currentPlacementDef;
            machine.grid = grid;
            machine.footprint = currentPlacementDef.footprint;
            machine.rotation = currentRotation;
            machine.anchorCell = anchorCell;
            machine.portDef = currentPlacementDef.connectionPorts;

            Vector3 center = GetFootprintCenterWorld(footprintCells);
            if (center == Vector3.zero)
                center = grid.CellToWorld(anchorCell);

            instance.transform.position = center;
            instance.transform.rotation = Quaternion.Euler(0f, 0f, -90f * currentRotation);
            instance.transform.localScale = Vector3.one * currentPlacementDef.placedSpriteScale;

            var spriteRenderer = instance.GetComponent<SpriteRenderer>();
            if (spriteRenderer == null)
                spriteRenderer = instance.AddComponent<SpriteRenderer>();

            spriteRenderer.sprite = currentPlacementDef.sprite;
            spriteRenderer.color = Color.white;
            spriteRenderer.sortingOrder = currentPlacementDef.placedSortingOrder;

            return machine;
        }

        private void SetFootprintHighlights(IReadOnlyList<Vector2Int> cells, bool valid)
        {
            EnsureFootprintHighlightPool(cells.Count);
            Color color = valid ? highlightTint : new Color(1f, 0f, 0f, highlightTint.a);
            for (int i = 0; i < cells.Count; i++)
            {
                var rend = footprintHighlights[i];
                rend.color = color;
                rend.gameObject.SetActive(true);
                rend.transform.position = new Vector3(cells[i].x + 0.5f, cells[i].y + 0.5f, 0f);
            }

            UpdatePlacementPreview(cells);

            for (int i = cells.Count; i < footprintHighlights.Count; i++)
            {
                footprintHighlights[i].gameObject.SetActive(false);
            }
        }

        private void UpdatePlacementPreview(IReadOnlyList<Vector2Int> cells)
        {
            if (currentPlacementDef == null || currentPlacementDef.sprite == null)
            {
                SetPlacementPreviewActive(false);
                return;
            }

            EnsurePlacementPreview();

            placementPreviewRenderer.sprite = currentPlacementDef.sprite;
            placementPreviewRenderer.color = new Color(1f, 1f, 1f, 0.5f);
            placementPreviewRenderer.sortingOrder = currentPlacementDef.placedSortingOrder;
            placementPreviewRenderer.transform.localScale = Vector3.one * currentPlacementDef.placedSpriteScale;
            placementPreviewRenderer.transform.rotation = Quaternion.Euler(0f, 0f, -90f * currentRotation);
            placementPreviewRenderer.transform.position = GetFootprintCenterWorld(cells);
            SetPlacementPreviewActive(true);
        }

        private void EnsureFootprintHighlightPool(int count)
        {
            while (footprintHighlights.Count < count)
            {
                var go = new GameObject("footprintHighlight");
                go.transform.SetParent(transform, worldPositionStays: true);
                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = highlightSprite;
                renderer.color = highlightTint;
                renderer.sortingLayerName = highlightSortingLayer;
                renderer.sortingOrder = highlightSortingOrder;
                go.transform.localScale = new Vector3(highlightScale.x, highlightScale.y, 1f);
                footprintHighlights.Add(renderer);
            }
        }

        private void EnsurePlacementPreview()
        {
            if (placementPreviewObject == null)
            {
                placementPreviewObject = new GameObject("componentPreview");
                placementPreviewObject.transform.SetParent(transform, worldPositionStays: true);
            }

            placementPreviewRenderer = placementPreviewObject.GetComponent<SpriteRenderer>();
            if (placementPreviewRenderer == null)
                placementPreviewRenderer = placementPreviewObject.AddComponent<SpriteRenderer>();

            placementPreviewRenderer.sortingLayerName = highlightSortingLayer;
        }

        private void SetFootprintHighlightsActive(bool active)
        {
            for (int i = 0; i < footprintHighlights.Count; i++)
                footprintHighlights[i].gameObject.SetActive(active);
            if (!active)
                SetPlacementPreviewActive(false);
        }

        private void SetPlacementPreviewActive(bool active)
        {
            if (placementPreviewObject != null)
                placementPreviewObject.SetActive(active);
        }

        private void CancelPlacement(bool returnItemToInventory)
        {
            ResetPlacementState(returnItemToInventory);
            GameModeManager.Instance?.SetMode(GameMode.Selection);
        }

        private void ExitPlacementMode()
        {
            ResetPlacementState(returnItem: false);
            GameModeManager.Instance?.SetMode(GameMode.Selection);
        }

        private void ResetPlacementState(bool returnItem)
        {
            if (returnItem && !string.IsNullOrEmpty(currentPlacementItemId) && inventory != null)
            {
                inventory.AddItem(currentPlacementItemId, 1);
            }

            currentPlacementDef = null;
            currentPlacementItemId = null;
            currentRotation = 0;

            ClearPlacementVisuals();
        }

        private void RefundPendingPlacement()
        {
            // Used when we leave placement via external means (mode change, disable) without placing.
            if (currentPlacementDef == null) return;
            ResetPlacementState(returnItem: true);
        }

        private void ClearPlacementVisuals()
        {
            SetFootprintHighlightsActive(false);
            highlightLastPosition = new Vector2Int(int.MinValue, int.MinValue);
            if (highlightObject != null)
            {
                // Keep the hover highlight hidden when exiting placement; Selection will re-enable as needed.
                highlightObject.SetActive(false);
            }
        }
        #endregion

    }
}


