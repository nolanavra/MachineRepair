# MachineRepair Unity Scene Wireup

This guide walks through wiring every script in this repository into a clean Unity scene so placement, wiring, simulation, and UI all work together deterministically.

## Prerequisites
- Unity 2022+ with the **New Input System** enabled.
- A Tilemap-based grid with tiles that distinguish `normalCell`, `connectorCell`, and `displayCell` usage.
- A `Gameplay` input actions asset that defines `Point`, `PrimaryClick`, `SecondaryClick`, `RotatePlacement`, `ModeSelection`, `ModeComponentPlacement`, `ModeWirePlacement`, `ModePipePlacement`, `ModeSimulation`, `ToggleInventory`, and `ToggleDebugUI`.

## 1) Author data assets
1. Create `ThingDef` ScriptableObjects via **Create > EspressoGrid > ThingDef** for every placeable part. Fill out identity, footprintMask (including `connectedPorts`), capability flags (power/water/signals), and sprites.
2. If you use flavor content, create assets under the `MachineRepair/Flavor/...` menus and reference them from your ThingDefs as needed.

## 2) Build core scene objects
1. Create an empty **GameController** object.
   - Add **PlayerInput** pointing to the `Gameplay` input actions.
   - Add **GameModeManager** (choose `initialMode` in the inspector).
   - Add **Inventory** and populate `inventoryCatalog` with your ThingDefs; set `slotCount`.
   - Add **SimulationManager** and assign the GridManager reference once it exists.
2. Create a **Grid** object with a **GridManager** component.
   - Assign the Tilemap that holds your layout to `tilemap`.
   - Optional: enable `enableCellHighlights`, assign `cellHighlightSprite`, and set the highlight parent.

## 3) Add placement and routing tools
1. Add **WirePlacementTool** to any organizer object.
   - Set `grid`, `playerInput`, and `wirePreviewPrefab` (LineRenderer) references.
   - Tune `wireColor`, widths, and simulation defaults if desired.
2. Add **PipePlacementTool** if you intend to place pipes; assign `grid`, `playerInput`, and pipe preview assets.
3. Add **InputRouter** (UI root or controller object is fine).
   - Assign `playerInput`, `grid`, `wireTool`, and `pipeTool`.
   - Set `highlightSprite` and tweak highlight sorting/tint options.
   - Leave `blockWhenPointerOverUI` enabled to prevent accidental placement through UI.
4. (Optional) Add **InputActionMapEnabler** if you want a helper that enables the `Gameplay` map on start.

## 4) Configure rendering helpers
- Add **CellRenderer** to an object that should draw placed sprites/overlays; assign `grid`, sprite references, and sorting layers.
- If you need consistent icon sizing in UI/world overlays, use **ForceSpriteSize** on the relevant renderers.

## 5) Wire up UI
1. Create a Canvas (Screen Space - Overlay recommended).
2. Add **InventoryUI** to a UI panel.
   - Assign `inventory`, `inventoryPanel`, `slotPrefab` (with `InventorySlotUI`), `gridLayout`, `playerInput`, and `inputRouter`.
3. Add **InspectorUI** and hook its text/image fields plus `inputRouter` so it refreshes when selections change.
4. Add **SimulationUI** to the panel that owns power/water controls.
   - Assign `simulationManager`, `gridManager`, `gameModeManager`, `startPowerButton`, `startWaterButton`, `powerLabel`, and `waterLabel`.
   - Optional: provide `pipeArrowPrefab`/parent and `leakSpritePrefab`/parent for flow/leak visuals; adjust animation speeds in the inspector.
5. (Optional) Add **DebugUI** and **DebugInventory** for quick inspection and spawning; point them at the same SimulationManager, Inventory, and GridManager.

## 6) Connect scene references
1. In **SimulationManager**, set `grid` to the GridManager object.
2. In **GameModeManager**, ensure it registers on Awake (default) and verify the hotkey bindings match your PlayerInput actions.
3. In **Inventory**, reference all ThingDefs you want available; InventoryUI will reflect the catalog automatically.
4. In **InputRouter**, confirm the `Gameplay` action map and action names match your input asset; leave them at defaults if you followed the prerequisites.
5. In **WirePlacementTool**/**PipePlacementTool**, confirm their `grid` and `playerInput` references are set so click routing works.

## 7) Quick validation pass
- Enter Play Mode, switch modes using the mode hotkeys, and verify the Inspector/Inventory UI responds.
- Place a component from the Inventory UI; GridManager should highlight valid cells and reject blocked ones.
- Switch to Wire Placement, click two compatible power ports, and confirm the preview/final wire renders.
- Enter Simulation mode; toggle power/water via SimulationUI and watch leak/flow overlays if configured.

## Notes
- Prefabs should **not** author footprint, origin, or ports; those come from ThingDef. The runtime pulls footprint/port data from the ThingDef when stamping components.
- GridManager remains the authority for cell occupancy and connector registration; avoid bypassing it for spawning or deletion.
- Keep world space aligned so Tilemap origin matches Z=0; UI blocks pointer routing when `blockWhenPointerOverUI` is true.
