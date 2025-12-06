# MachineRepair Wiring Guide

This repository contains a Unity 2D prototype for wiring up an espresso-machine simulation. The notes below explain how to connect every major system in a fresh scene so input, placement, simulation, and UI all talk to each other.

## Prerequisites
- Unity 2022 or newer with the **New Input System** enabled.
- A `PlayerInput` component using an action map named `Gameplay` with the following actions defined (value/type):
  - `Point` (Vector2, Pass-Through)
  - `PrimaryClick` (Button)
  - `SecondaryClick` (Button)
  - `RotatePlacement` (Button or Value)
  - `ModeSelection`, `ModeComponentPlacement`, `ModeWirePlacement`, `ModePipePlacement`, `ModeSimulation` (Buttons)
  - `ToggleInventory` (Button)
- At least one Tilemap that represents the playable grid; tiles named `normalCell`, `connectorCell`, and `displayCell` gate placement permissions.

## Scene wiring (top-level objects)
1. **GameController (empty object)**
   - Add **PlayerInput** and assign your `Gameplay` input actions asset.
   - Add **GameModeManager** (sets default `initialMode` in the inspector). It will register listeners automatically.
   - Add **Inventory** and populate `inventoryCatalog` with `ThingDef` entries; set `slotCount` to your desired starting capacity.
   - Add **SimulationManager** and point `grid` to the GridManager instance.

2. **Grid**
   - Create a GameObject named `Grid` and add **GridManager**.
   - Assign the Tilemap that holds your layout to `tilemap`.
   - (Optional) Enable `enableCellHighlights` and assign `cellHighlightSprite` plus a parent transform for overlay sprites.

3. **Input routing & placement**
   - Add **InputRouter** to a suitable object (UI root or controller).
   - Inspector references:
     - `playerInput`: the PlayerInput from GameController.
     - `grid`: the GridManager instance.
     - `inventory`: Inventory instance (for consuming items when placing components).
     - `wireTool`: the WirePlacementTool instance.
     - `highlightSprite`: sprite used for cell hover highlights (plus tint/sorting options).
   - InputRouter registers with GameModeManager and drives click routing for selection, component placement, and wire/pipes. Use `BeginComponentPlacement` to start a placement from code or UI.

4. **Component placement definitions**
   - `ThingDef` ScriptableObjects define each placeable component. Set footprint, visuals, and `portDefs` for connection points.
   - `PortDef` defines port positions and `WireType` acceptance for wires.

5. **Wire placement**
   - Add **WirePlacementTool** to an object.
   - Inspector references:
     - `grid`: GridManager.
     - `playerInput`: PlayerInput (for `Point` action when previewing).
     - `wirePreviewPrefab`: a LineRenderer prefab for preview lines; set width and material as desired.
   - Optional tuning: `wireColor`, `wireType`, preview Z offset, line width, and simulation defaults for resistance/current limits.
   - InputRouter calls `HandleClick` in Wire mode; first click starts a preview, second finalizes a wire between compatible power ports.

6. **UI wiring**
   - **SimpleInventoryUI**: place on a UI object with a `GridLayoutGroup`.
     - References: `inventory`, `inventoryPanel` (root panel to toggle), `inputRouter`, `playerInput`, `slotPrefab`, and `gridLayout`.
     - The slot prefab should expose an `InventorySlotUI` component with Icon/Count child elements. The `ToggleInventory` action hides/shows `inventoryPanel`.
   - **InspectorUI**: listens for selection updates from InputRouter and displays component info. Assign text/image fields to match your UI layout.
   - **SimulationUI**: attach to UI that holds the power/water buttons and leak/flow overlays.
     - References: `simulationManager`, `gridManager`, `gameModeManager`, `startPowerButton`, `startWaterButton`, `powerLabel`, `waterLabel`.
     - Optional visuals: `pipeArrowPrefab` + parent, `leakSpritePrefab` + parent, and tweak arrow/leak animation speeds.
   - **DebugUI/DebugInventory** (optional): hook to SimulationManager and Inventory for quick testing.

7. **Rendering helpers**
   - **CellRenderer** paints placed components and overlays by reading GridManager occupancy.
   - **WireColorUI** is deprecated; prefer InputRouter + WirePlacementTool for live wire feedback.
   - **ForceSpriteSize** ensures sprites render at consistent cell sizes in UI or world space.

## Expected play loop
1. Use mode hotkeys (from GameModeManager) to switch between Selection, Component Placement, Wire Placement, Pipe Placement, and Simulation modes.
2. InventoryUI triggers InputRouter.BeginComponentPlacement when clicking a slot; left-click a valid grid cell to place.
3. Enter Wire Placement mode and click two compatible power ports to lay a wire path; preview follows the cursor after the first click.
4. Switch to Simulation mode; SimulationManager autoruns steps and SimulationUI buttons toggle power/water. Leaks and flow arrows are spawned via SimulationUI when the simulation reports them.

## Tips
- Most components auto-discover references (PlayerInput, GridManager, Inventory) if left unassigned, but explicit inspector wiring makes scene setup deterministic.
- All systems assume world-space coordinates match the Tilemap’s origin with Z = 0.
- Keep UI canvases in Screen Space - Overlay or Screen Space - Camera; InputRouter blocks placement when the pointer is over UI if `blockWhenPointerOverUI` is enabled.
