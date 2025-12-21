using MachineRepair;
using MachineRepair.Grid;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using static MachineRepair.Grid.GridManager;


namespace MachineRepair.Grid
{
    public class GridManager : MonoBehaviour
    {
        [Header("Map Size")]
        public int width = 64;
        public int height = 48;
        public event Action GridTopologyChanged;

        [Header("Cells")]
        private CellPlaceability nullCellPlaceability;
        private CellPlaceability normalCellPlaceability;
        private CellPlaceability connectorCellPlaceability;
        private CellPlaceability displayCellPlaceability;
        private CellPlaceability[] basePlaceabilityByIndex;
        private CellTerrain[] terrainByIndex;
        private CellOccupancy[] occupancyByIndex;
        public cellDef[] cellSubGrid;

        public readonly struct FootprintCells
        {
            public readonly List<Vector2Int> OccupiedCells;
            public readonly List<Vector2Int> DisplayCells;

            public FootprintCells(List<Vector2Int> occupiedCells, List<Vector2Int> displayCells)
            {
                OccupiedCells = occupiedCells ?? new List<Vector2Int>();
                DisplayCells = displayCells ?? new List<Vector2Int>();
            }
        }


        [Header("Spills (0..n liters depth proxy)")]
        private float[] spillByIndex;

        [Header("Overlays")]
        private bool[] powerByIndex; // true if this cell has power service
        private bool[] waterByIndex; // true if this cell has water service

        [Header("Things")]
        private List<ThingDef>[] bucketByIndex;  // 0..many per cell

        [Header("Tilemap to query")]
        [SerializeField] private Tilemap tilemap;

        [Header("Component Placement from Tilemap")]
        [SerializeField] private Tilemap componentTilemap;
        [SerializeField] private ThingDef chassisPowerConnectionDef;
        [SerializeField] private ThingDef chassisWaterConnectionDef;
        [SerializeField] private List<ComponentPrefabMapping> componentPrefabs = new();

        [Header("Placement")]
        [SerializeField] private Inventory inventory;
        [SerializeField] private Sprite placementHighlightSprite;
        [SerializeField] private Sprite placementPreviewSpriteOverride;
        [SerializeField] private string placementHighlightSortingLayer = "Default";
        [SerializeField] private int placementHighlightSortingOrder = 1000;
        [SerializeField] private Vector2 placementHighlightScale = new Vector2(1f, 1f);
        [SerializeField] private Color placementValidTint = new Color(1f, 1f, 0f, 0.25f);
        [SerializeField] private Color placementDisplayRequirementTint = new Color(1f, 0.65f, 0f, 0.25f);
        [SerializeField] private Color placementInvalidTint = new Color(1f, 0f, 0f, 0.25f);

        [Header("Port Markers")]
        [SerializeField] private Sprite portMarkerSprite;
        [SerializeField] private string portMarkerSortingLayer = "Default";
        [SerializeField] private int portMarkerSortingOrder = 950;
        [SerializeField] private Color portMarkerPowerColor = new Color(0.95f, 0.3f, 0.3f, 0.9f);
        [SerializeField] private Color portMarkerWaterColor = new Color(0.25f, 0.55f, 1f, 0.9f);
        [SerializeField] private Color portMarkerSignalColor = new Color(0.75f, 0.45f, 0.95f, 0.9f);

        public int CellCount => width * height;
        public bool setup = false;

        [Header("Cell Highlights")]
        [SerializeField] private bool enableCellHighlights = false;
        [SerializeField] private Sprite cellHighlightSprite;
        [SerializeField] private Transform cellHighlightParent;
        [SerializeField] private Transform subGridHighlightParent;
        [SerializeField] private string cellHighlightSortingLayer = "Default";
        [SerializeField] private int cellHighlightSortingOrder = 0;
        [SerializeField] private Color placeableColor = new Color(0.25f, 0.9f, 0.25f, 0.2f);
        [SerializeField] private Color connectorsOnlyColor = new Color(0.2f, 0.6f, 1f, 0.25f);
        [SerializeField] private Color displayColor = new Color(0.9f, 0.8f, 0.2f, 0.2f);
        [SerializeField] private Color blockedColor = new Color(0.6f, 0.6f, 0.6f, 0.15f);
        [SerializeField] private Color componentContentColor = new Color(1f, 0.55f, 0.2f, 0.35f);
        [SerializeField] private Color wireContentColor = new Color(0.25f, 0.9f, 0.9f, 0.35f);
        [SerializeField] private Color pipeContentColor = new Color(0.75f, 0.55f, 1f, 0.35f);
        [SerializeField] private Color mixedContentColor = new Color(0.95f, 0.35f, 0.35f, 0.35f);
        [SerializeField] private bool mirrorHighlightsToSubGrid = true;
        [SerializeField] private Vector3 subGridHighlightOffset = new Vector3(0f, -50f, 0f);

        [Header("Display Sprites")]
        [SerializeField] private Vector3 subGridDisplayOffset = new Vector3(0f, -50f, 0f);
        [SerializeField] private string displaySpriteSortingLayer = "Default";
        [SerializeField] private int displaySpriteSortingOrder = 400;

        private SpriteRenderer[] cellHighlights;
        private SpriteRenderer[] subGridHighlights;

        private ThingDef currentPlacementDef;
        private string currentPlacementItemId;
        private int currentPlacementRotation;
        private readonly List<SpriteRenderer> placementHighlights = new();
        private GameObject placementPreviewObject;
        private SpriteRenderer placementPreviewRenderer;

        private Dictionary<ThingDef, GameObject> componentPrefabByDef;
        private Dictionary<string, GameObject> componentPrefabByName;

        private void RaiseGridTopologyChanged()
        {
            GridTopologyChanged?.Invoke();
        }

        private void EnsureComponentPrefabMappings()
        {
            componentPrefabs ??= new List<ComponentPrefabMapping>();

            bool mappingMissing = componentPrefabs.Count == 0;
            var existingDefs = new HashSet<ThingDef>();
            var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < componentPrefabs.Count; i++)
            {
                var entry = componentPrefabs[i];
                if (entry == null || entry.prefab == null)
                {
                    mappingMissing = true;
                    continue;
                }

                if (entry.def != null)
                {
                    if (!existingDefs.Add(entry.def))
                    {
                        Debug.LogWarning($"GridManager: Duplicate ThingDef mapping for '{entry.def.defName ?? entry.def.name}'. Skipping extras.");
                    }
                }

                var keyName = !string.IsNullOrWhiteSpace(entry.defNameOverride)
                    ? entry.defNameOverride
                    : entry.def != null ? entry.def.defName : null;

                if (!string.IsNullOrWhiteSpace(keyName))
                {
                    if (!existingNames.Add(keyName))
                    {
                        Debug.LogWarning($"GridManager: Duplicate defName mapping for '{keyName}'. Skipping extras.");
                    }
                }
            }

            if (!mappingMissing)
            {
                return;
            }

            var resourcePrefabs = Resources.LoadAll<GameObject>("Components");
            foreach (var prefab in resourcePrefabs)
            {
                var machine = prefab != null ? prefab.GetComponent<MachineComponent>() : null;
                var def = machine != null ? machine.def : null;
                var defName = def != null ? def.defName : null;

                if (def == null)
                {
                    Debug.LogWarning($"GridManager: Prefab '{prefab?.name}' in Resources/Components is missing a ThingDef reference.");
                    continue;
                }

                bool defAlreadyMapped = existingDefs.Contains(def);
                bool nameAlreadyMapped = !string.IsNullOrWhiteSpace(defName) && existingNames.Contains(defName);

                if (defAlreadyMapped || nameAlreadyMapped)
                {
                    continue;
                }

                var mapping = new ComponentPrefabMapping
                {
                    def = def,
                    defNameOverride = string.IsNullOrWhiteSpace(defName) ? null : defName,
                    prefab = prefab
                };

                componentPrefabs.Add(mapping);
                existingDefs.Add(def);
                if (!string.IsNullOrWhiteSpace(defName)) existingNames.Add(defName);
            }
        }

        [Serializable]
        private class ComponentPrefabMapping
        {
            public ThingDef def;
            public string defNameOverride;
            public GameObject prefab;
        }

        private void Start()
        {

        }
        void Awake()
        {
            if (inventory == null) inventory = FindFirstObjectByType<Inventory>();
            BuildComponentPrefabLookup();
            cellDefByType();
            InitGrids();
            setup = true;

            EnsureHighlightParent();
            BuildCellHighlightPool();

        }

        private void OnValidate()
        {
            BuildComponentPrefabLookup();
#if UNITY_EDITOR
            ValidateComponentPrefabsInEditor();
#endif
        }

        private void Update()
        {
            if (!enableCellHighlights) return;
            if (!setup) return;

            if (cellHighlights == null || cellHighlights.Length != CellCount)
            {
                BuildCellHighlightPool();
            }

            RefreshCellHighlights();
        }

        #region API: GRID
        // --- Grid Setup
        public void InitGrids()
        {
            int n = CellCount;
            basePlaceabilityByIndex = new CellPlaceability[n];
            terrainByIndex = new CellTerrain[n];
            occupancyByIndex = new CellOccupancy[n];
            spillByIndex = new float[n];
            powerByIndex = new bool[n];
            waterByIndex = new bool[n];
            bucketByIndex = new List<ThingDef>[n];

            for (int i = 0; i < terrainByIndex.Length; i++)
            {
                var occupancy = new CellOccupancy();
                occupancy.Clear();
                occupancyByIndex[i] = occupancy;
                bucketByIndex[i] = new List<ThingDef>();


                var (x, y) = FromIndex(i);
                Vector3Int cellPos = new Vector3Int(x, y, 0);  // z=0 for tilemap

                // grab the tile (TileBase covers Tile/RuleTile/etc.)
                TileBase t = tilemap.GetTile(cellPos);

                var placeability = ResolvePlaceability(t);
                basePlaceabilityByIndex[i] = placeability;
                terrainByIndex[i] = new CellTerrain
                {
                    index = i,
                    placeability = placeability,
                    isDisplayZone = placeability == CellPlaceability.Display
                };
            }

            PlaceComponentsFromTilemap();
            UpdateSubGrid();
        }

        private void PlaceComponentsFromTilemap()
        {
            if (componentTilemap == null) return;

            foreach (var pos in componentTilemap.cellBounds.allPositionsWithin)
            {
                var cellPos = new Vector3Int(pos.x, pos.y, 0);
                if (!componentTilemap.HasTile(cellPos)) continue;

                var tile = componentTilemap.GetTile(cellPos);
                if (tile == null || string.IsNullOrWhiteSpace(tile.name)) continue;

                Vector2Int gridCell = new Vector2Int(cellPos.x, cellPos.y);
                if (!InBounds(gridCell.x, gridCell.y)) continue;

                ThingDef def = tile.name switch
                {
                    nameof(ComponentType.ChassisPowerConnection) => chassisPowerConnectionDef,
                    nameof(ComponentType.ChassisWaterConnection) => chassisWaterConnectionDef,
                    _ => null
                };

                if (def == null) continue;

                var footprintCells = GetFootprintCells(gridCell, def.footprintMask);
                if (!IsFootprintPlaceable(footprintCells)) continue;

                var component = CreateComponentInstance(def, gridCell);
                if (TryPlaceComponentFootprint(component, footprintCells))
                {
                    ApplyPortMarkers(component);
                    ApplyDisplaySprites(component, footprintCells.DisplayCells, def.footprintMask, gridCell, 0);
                    RaiseGridTopologyChanged();
                }
            }
        }

        private bool TryPlaceComponentFootprint(MachineComponent component, FootprintCells cells)
        {
            if (component == null) return false;

            var placementCells = cells.OccupiedCells;
            bool usingDisplayCells = placementCells == null || placementCells.Count == 0;

            HashSet<Vector2Int> displayCellSet = null;
            if (cells.DisplayCells != null && cells.DisplayCells.Count > 0)
            {
                displayCellSet = new HashSet<Vector2Int>(cells.DisplayCells);
                if (usingDisplayCells) placementCells = cells.DisplayCells;
            }

            if (placementCells == null || placementCells.Count == 0) return false;

            bool markTerrainConnectorsOnly = !usingDisplayCells;
            for (int i = 0; i < placementCells.Count; i++)
            {
                var cell = placementCells[i];
                bool isDisplayCell = displayCellSet != null && displayCellSet.Contains(cell);
                bool markConnectorsOnly = markTerrainConnectorsOnly && !isDisplayCell;

                if (!TryPlaceComponent(cell, component, markConnectorsOnly, allowDisplayCell: isDisplayCell))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsFootprintPlaceable(FootprintCells cells)
        {
            bool hasAnyCells = (cells.OccupiedCells != null && cells.OccupiedCells.Count > 0)
                || (cells.DisplayCells != null && cells.DisplayCells.Count > 0);
            if (!hasAnyCells) return false;

            HashSet<Vector2Int> displayCells = null;
            if (cells.DisplayCells != null && cells.DisplayCells.Count > 0)
            {
                displayCells = new HashSet<Vector2Int>(cells.DisplayCells);
            }

            if (cells.DisplayCells != null)
            {
                for (int i = 0; i < cells.DisplayCells.Count; i++)
                {
                    var c = cells.DisplayCells[i];
                    if (!InBounds(c.x, c.y)) return false;

                    int idx = ToIndex(c);
                    var terrain = terrainByIndex[idx];
                    if (terrain.placeability != CellPlaceability.Display) return false;
                    if (occupancyByIndex[idx].HasComponent) return false;
                }
            }

            if (cells.OccupiedCells != null)
            {
                for (int i = 0; i < cells.OccupiedCells.Count; i++)
                {
                    var c = cells.OccupiedCells[i];
                    if (displayCells != null && displayCells.Contains(c)) continue;
                    if (!InBounds(c.x, c.y)) return false;

                    int idx = ToIndex(c);
                    var terrain = terrainByIndex[idx];
                    if (terrain.placeability == CellPlaceability.Blocked
                        || terrain.placeability == CellPlaceability.ConnectorsOnly
                        || terrain.placeability == CellPlaceability.Display)
                        return false;

                    if (occupancyByIndex[idx].HasComponent)
                        return false;
                }
            }

            return true;
        }

        private readonly struct FootprintValidationResult
        {
            public readonly bool IsValid;
            public readonly bool DisplayRequirementFailed;

            public FootprintValidationResult(bool isValid, bool displayRequirementFailed)
            {
                IsValid = isValid;
                DisplayRequirementFailed = displayRequirementFailed;
            }
        }

        private FootprintValidationResult ValidateFootprint(FootprintCells cells)
        {
            bool hasAnyCells = (cells.OccupiedCells != null && cells.OccupiedCells.Count > 0)
                || (cells.DisplayCells != null && cells.DisplayCells.Count > 0);
            if (!hasAnyCells) return new FootprintValidationResult(false, false);

            HashSet<Vector2Int> displayCellSet = null;
            if (cells.DisplayCells != null && cells.DisplayCells.Count > 0)
            {
                displayCellSet = new HashSet<Vector2Int>(cells.DisplayCells);
            }

            if (cells.DisplayCells != null)
            {
                for (int i = 0; i < cells.DisplayCells.Count; i++)
                {
                    Vector2Int c = cells.DisplayCells[i];
                    if (!InBounds(c.x, c.y)) return new FootprintValidationResult(false, true);
                    var cell = GetCell(c);
                    if (cell.placeability != CellPlaceability.Display) return new FootprintValidationResult(false, true);
                    if (cell.HasComponent) return new FootprintValidationResult(false, true);
                }
            }

            if (cells.OccupiedCells != null)
            {
                for (int i = 0; i < cells.OccupiedCells.Count; i++)
                {
                    Vector2Int c = cells.OccupiedCells[i];
                    if (displayCellSet != null && displayCellSet.Contains(c)) continue;
                    if (!InBounds(c.x, c.y)) return new FootprintValidationResult(false, false);
                    var cell = GetCell(c);
                    if (cell.placeability == CellPlaceability.Blocked ||
                        cell.placeability == CellPlaceability.ConnectorsOnly ||
                        cell.placeability == CellPlaceability.Display)
                        return new FootprintValidationResult(false, false);
                    if (cell.HasComponent) return new FootprintValidationResult(false, false);
                }
            }

            return new FootprintValidationResult(true, false);
        }

        public FootprintCells GetFootprintCells(Vector2Int anchorCell, FootprintMask footprint, int rotationSteps = 0)
        {
            var occupiedCells = new List<Vector2Int>();
            var displayCells = new List<Vector2Int>();
            if (footprint.occupied == null) return new FootprintCells(occupiedCells, displayCells);

            for (int y = 0; y < footprint.height; y++)
            {
                for (int x = 0; x < footprint.width; x++)
                {
                    int idx = y * footprint.width + x;
                    bool occupied = footprint.occupied[idx];
                    bool display = footprint.display != null && footprint.display.Length > idx && footprint.display[idx];

                    if (!occupied && !display) continue;

                    Vector2Int local = new Vector2Int(x - footprint.origin.x, y - footprint.origin.y);
                    Vector2Int rotated = RotateOffset(local, rotationSteps);
                    Vector2Int cell = anchorCell + rotated;

                    if (occupied) occupiedCells.Add(cell);
                    if (display && !displayCells.Contains(cell)) displayCells.Add(cell);
                }
            }

            return new FootprintCells(occupiedCells, displayCells);
        }

        public FootprintCells GetFootprintCells(Vector2Int anchorCell, ThingDef def, int rotationSteps = 0)
        {
            if (def == null) return new FootprintCells(new List<Vector2Int>(), new List<Vector2Int>());
            return GetFootprintCells(anchorCell, def.footprintMask, rotationSteps);
        }

        private FootprintCells GetFootprintCells(MachineComponent component)
        {
            if (component == null || component.footprint.occupied == null) return new FootprintCells(new List<Vector2Int>(), new List<Vector2Int>());

            return GetFootprintCells(component.anchorCell, component.footprint, component.rotation);
        }

        private static List<Vector2Int> CollectAllFootprintCells(FootprintCells cells)
        {
            var allCells = new List<Vector2Int>();
            if (cells.OccupiedCells != null)
            {
                allCells.AddRange(cells.OccupiedCells);
            }

            if (cells.DisplayCells != null)
            {
                for (int i = 0; i < cells.DisplayCells.Count; i++)
                {
                    var displayCell = cells.DisplayCells[i];
                    if (!allCells.Contains(displayCell)) allCells.Add(displayCell);
                }
            }

            return allCells;
        }

        private Vector3 GetFootprintCenterWorld(FootprintCells cells)
        {
            var allCells = CollectAllFootprintCells(cells);
            if (allCells.Count == 0) return Vector3.zero;

            Vector3 sum = Vector3.zero;
            for (int i = 0; i < allCells.Count; i++)
            {
                sum += CellToWorld(allCells[i]);
            }

            return sum / allCells.Count;
        }

        private MachineComponent CreateComponentInstance(ThingDef def, Vector2Int anchorCell)
        {
            return CreateComponentInstance(def, anchorCell, 0, CellToWorld(anchorCell));
        }

        private MachineComponent CreateComponentInstance(ThingDef def, Vector2Int anchorCell, int rotationSteps, Vector3 worldPosition)
        {
            if (def == null)
            {
                Debug.LogError("GridManager: Cannot create a component for a null ThingDef.");
                return null;
            }

            GameObject prefab = ResolveComponentPrefab(def);
            if (prefab == null)
            {
                Debug.LogError($"GridManager: No component prefab configured for def '{def.defName ?? def.name}'.");
                return null;
            }

            GameObject instance = Instantiate(prefab);
            instance.name = def.displayName ?? def.defName ?? instance.name;

            var machine = instance.GetComponent<MachineComponent>();
            if (machine == null)
            {
                Debug.LogError($"GridManager: Prefab '{prefab.name}' is missing MachineComponent for def '{def.defName}'.");
                if (Application.isPlaying)
                    Destroy(instance);
                else
                    DestroyImmediate(instance);
                return null;
            }

            machine.def = def;
            machine.grid = this;
            machine.footprint = def.footprintMask;
            machine.rotation = rotationSteps;
            machine.anchorCell = anchorCell;
            machine.portDef = def.footprintMask.connectedPorts;

            instance.transform.position = worldPosition;
            instance.transform.rotation = Quaternion.Euler(0f, 0f, -90f * rotationSteps);
            var renderer = instance.GetComponent<SpriteRenderer>() ?? instance.GetComponentInChildren<SpriteRenderer>();
            var targetTransform = renderer != null ? renderer.transform : instance.transform;
            Vector3 scale = CalculateSpriteScale(
                renderer != null ? renderer.sprite : def.sprite,
                def.footprintMask,
                rotationSteps,
                def.placedSpriteScale,
                def.constrainPlacedSpriteToFootprint,
                targetTransform.localScale);
            targetTransform.localScale = scale;

            ApplyPortMarkers(machine);

            return machine;
        }

        private Vector3 CalculateSpriteScale(
            Sprite sprite,
            FootprintMask footprintMask,
            int rotationSteps,
            float scaleMultiplier,
            bool constrainToFootprint,
            Vector3 baseScale)
        {
            Vector3 scale = baseScale;

            if (constrainToFootprint
                && sprite != null
                && MachineComponent.TryGetFootprintSize(footprintMask, rotationSteps, out var footprintSize))
            {
                Vector2 spriteSize = sprite.bounds.size;
                if (spriteSize.x > 0f && spriteSize.y > 0f)
                {
                    scale = new Vector3(
                        footprintSize.x / spriteSize.x,
                        footprintSize.y / spriteSize.y,
                        1f);
                }
            }

            return scale * scaleMultiplier;
        }

        private void BuildComponentPrefabLookup()
        {
            EnsureComponentPrefabMappings();

            componentPrefabByDef ??= new Dictionary<ThingDef, GameObject>();
            componentPrefabByName ??= new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);

            componentPrefabByDef.Clear();
            componentPrefabByName.Clear();

            if (componentPrefabs == null) return;

            for (int i = 0; i < componentPrefabs.Count; i++)
            {
                var entry = componentPrefabs[i];
                if (entry == null) continue;

                if (entry.prefab == null)
                {
                    var defLabel = entry.def?.defName ?? entry.defNameOverride ?? entry.def?.name;
                    Debug.LogWarning($"GridManager: ThingDef '{defLabel}' does not have a prefab assigned.");
                    continue;
                }

                if (entry.def != null)
                {
                    if (!componentPrefabByDef.ContainsKey(entry.def))
                        componentPrefabByDef.Add(entry.def, entry.prefab);
                    else
                        Debug.LogWarning($"GridManager: Duplicate prefab mapping for def '{entry.def.defName}'.");
                }

                string keyName = !string.IsNullOrWhiteSpace(entry.defNameOverride)
                    ? entry.defNameOverride
                    : entry.def != null ? entry.def.defName : null;

                if (!string.IsNullOrWhiteSpace(keyName))
                {
                    if (!componentPrefabByName.ContainsKey(keyName))
                        componentPrefabByName.Add(keyName, entry.prefab);
                    else
                        Debug.LogWarning($"GridManager: Duplicate prefab mapping for defName '{keyName}'.");
                }
            }
        }

#if UNITY_EDITOR
        private void ValidateComponentPrefabsInEditor()
        {
            if (Application.isPlaying || componentPrefabs == null) return;

            foreach (var entry in componentPrefabs)
            {
                if (entry?.def != null && entry.prefab == null)
                {
                    Debug.LogWarning($"GridManager: ThingDef '{entry.def.defName ?? entry.def.name}' is missing a prefab mapping in this scene.");
                }
            }
        }
#endif

        private GameObject ResolveComponentPrefab(ThingDef def)
        {
            if (def == null) return null;

            if (componentPrefabByDef == null || componentPrefabByName == null)
            {
                BuildComponentPrefabLookup();
            }

            if (componentPrefabByDef != null
                && componentPrefabByDef.TryGetValue(def, out var prefab)
                && prefab != null)
            {
                return prefab;
            }

            if (!string.IsNullOrWhiteSpace(def.defName)
                && componentPrefabByName != null
                && componentPrefabByName.TryGetValue(def.defName, out prefab)
                && prefab != null)
            {
                return prefab;
            }

            return null;
        }

        public void ApplyPortMarkers(MachineComponent machine)
        {
            if (machine == null) return;
            machine.RefreshPortMarkers(
                this,
                portMarkerSprite,
                portMarkerSortingLayer,
                portMarkerSortingOrder,
                portMarkerPowerColor,
                portMarkerWaterColor,
                portMarkerSignalColor);
        }

        public void ApplyDisplaySprites(
            MachineComponent machine,
            IReadOnlyList<Vector2Int> displayCells,
            FootprintMask footprintMask,
            Vector2Int anchorCell,
            int rotationSteps)
        {
            if (machine == null)
            {
                return;
            }

            var sprite = machine.def != null ? machine.def.displaySprite : null;
            var displayTrim = machine.def != null
                ? new Vector3(machine.def.displayTrimX, machine.def.displayTrimY, 0f)
                : Vector3.zero;
            if (sprite == null || displayCells == null || displayCells.Count == 0)
            {
                machine.DestroyDisplaySprites();
                return;
            }

            machine.RefreshDisplaySprites(
                this,
                sprite,
                displayCells,
                subGridDisplayOffset + displayTrim,
                displaySpriteSortingLayer,
                displaySpriteSortingOrder,
                footprintMask,
                anchorCell,
                rotationSteps,
                machine.def == null || machine.def.constrainDisplaySpriteToFootprint);
        }

        #region Placement

        public bool IsPlacementActive => currentPlacementDef != null;

        public bool TryStartPlacement(string itemId)
        {
            if (string.IsNullOrEmpty(itemId) || inventory == null) return false;

            var def = inventory.GetDef(itemId);
            if (def == null) return false;

            if (!inventory.TryConsumeForPlacement(itemId)) return false;

            currentPlacementDef = def;
            currentPlacementItemId = itemId;
            currentPlacementRotation = 0;
            GameModeManager.Instance?.SetMode(GameMode.ComponentPlacement);
            return true;
        }

        public void RotatePlacement()
        {
            if (!IsPlacementActive) return;
            currentPlacementRotation = (currentPlacementRotation + 1) % 4;
        }

        public bool TryPlaceCurrent(Vector2Int anchorCell)
        {
            if (!IsPlacementActive) return false;

            var cells = GetFootprintCells(anchorCell, currentPlacementDef, currentPlacementRotation);
            var validation = ValidateFootprint(cells);
            if (!validation.IsValid) return false;

            Vector3 center = GetFootprintCenterWorld(cells);
            if (center == Vector3.zero) center = CellToWorld(anchorCell);

            MachineComponent machine = CreateComponentInstance(currentPlacementDef, anchorCell, currentPlacementRotation, center);
            if (machine == null) return false;

            if (!TryPlaceComponentFootprint(machine, cells))
            {
                if (Application.isPlaying)
                    Destroy(machine.gameObject);
                else
                    DestroyImmediate(machine.gameObject);
                return false;
            }

            ApplyDisplaySprites(
                machine,
                cells.DisplayCells,
                currentPlacementDef.footprintMask,
                anchorCell,
                currentPlacementRotation);
            RaiseGridTopologyChanged();
            SetPlacementHighlightsActive(false);
            ClearPlacementState(false);
            GameModeManager.Instance?.SetMode(GameMode.Selection);
            return true;
        }

        public void CancelPlacement(bool refundItem, bool revertMode = true)
        {
            if (!IsPlacementActive) return;
            if (refundItem && inventory != null)
            {
                inventory.RefundPlacementItem(currentPlacementItemId);
            }

            ClearPlacementState(true);
            if (revertMode)
            {
                GameModeManager.Instance?.SetMode(GameMode.Selection);
            }
        }

        public void UpdatePlacementPreview(Vector2Int pointerCell)
        {
            if (!IsPlacementActive)
            {
                SetPlacementHighlightsActive(false);
                return;
            }

            var cells = GetFootprintCells(pointerCell, currentPlacementDef, currentPlacementRotation);
            var validation = ValidateFootprint(cells);
            SetPlacementHighlights(cells, validation);
        }

        private void ClearPlacementState(bool clearVisuals)
        {
            currentPlacementDef = null;
            currentPlacementItemId = null;
            currentPlacementRotation = 0;

            if (clearVisuals)
            {
                SetPlacementHighlightsActive(false);
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

            placementPreviewRenderer.sortingLayerName = placementHighlightSortingLayer;
            placementPreviewRenderer.sortingOrder = placementHighlightSortingOrder;
        }

        private void EnsurePlacementHighlightPool(int count)
        {
            while (placementHighlights.Count < count)
            {
                var go = new GameObject("footprintHighlight");
                go.transform.SetParent(transform, worldPositionStays: true);
                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = placementHighlightSprite;
                renderer.color = placementValidTint;
                renderer.sortingLayerName = placementHighlightSortingLayer;
                renderer.sortingOrder = placementHighlightSortingOrder;
                go.transform.localScale = new Vector3(placementHighlightScale.x, placementHighlightScale.y, 1f);
                placementHighlights.Add(renderer);
            }
        }

        private void SetPlacementHighlights(FootprintCells cells, FootprintValidationResult validation)
        {
            var allCells = CollectAllFootprintCells(cells);
            if (allCells.Count == 0)
            {
                SetPlacementHighlightsActive(false);
                return;
            }

            EnsurePlacementHighlightPool(allCells.Count);
            Color color = validation.IsValid
                ? placementValidTint
                : validation.DisplayRequirementFailed
                    ? placementDisplayRequirementTint
                    : placementInvalidTint;

            for (int i = 0; i < allCells.Count; i++)
            {
                var rend = placementHighlights[i];
                rend.color = color;
                rend.gameObject.SetActive(true);
                rend.transform.position = new Vector3(allCells[i].x + 0.5f, allCells[i].y + 0.5f, 0f);
            }

            UpdatePlacementPreview(cells, color);

            for (int i = allCells.Count; i < placementHighlights.Count; i++)
            {
                placementHighlights[i].gameObject.SetActive(false);
            }
        }

        private void UpdatePlacementPreview(FootprintCells cells, Color tint)
        {
            if (currentPlacementDef == null)
            {
                SetPlacementPreviewActive(false);
                return;
            }

            EnsurePlacementPreview();

            placementPreviewRenderer.sprite = placementPreviewSpriteOverride != null
                ? placementPreviewSpriteOverride
                : currentPlacementDef.sprite;
            placementPreviewRenderer.color = new Color(tint.r, tint.g, tint.b, 0.5f);
            placementPreviewRenderer.sortingOrder = currentPlacementDef.placedSortingOrder;
            var previewBaseScale = Vector3.one;
            placementPreviewRenderer.transform.localScale = CalculateSpriteScale(
                placementPreviewRenderer.sprite,
                currentPlacementDef.footprintMask,
                currentPlacementRotation,
                currentPlacementDef.placedSpriteScale,
                currentPlacementDef.constrainPlacedSpriteToFootprint,
                previewBaseScale);
            placementPreviewRenderer.transform.rotation = Quaternion.Euler(0f, 0f, -90f * currentPlacementRotation);
            placementPreviewRenderer.transform.position = GetFootprintCenterWorld(cells);
            SetPlacementPreviewActive(true);
        }

        public void SetPlacementHighlightsActive(bool active)
        {
            for (int i = 0; i < placementHighlights.Count; i++)
                placementHighlights[i].gameObject.SetActive(active);
            if (!active)
                SetPlacementPreviewActive(false);
        }

        private void SetPlacementPreviewActive(bool active)
        {
            if (placementPreviewObject != null)
                placementPreviewObject.SetActive(active);
        }

        #endregion

        public void UpdateSubGrid()
        {
            int n = width;
            cellSubGrid = new cellDef [n];

            for (int i = 0; i < cellSubGrid.Length; i++)
            {
                Vector2Int m = new Vector2Int(i, 0);
                cellSubGrid[i] = BuildCellDef(ToIndex(m));

                Vector3Int cellPos = new Vector3Int(i, 0, 0);
                TileBase t = tilemap.GetTile(cellPos);
                if (t != null)
                {
                    Vector3Int subcellPos = new Vector3Int(i, -50, 0);
                    tilemap.SetTile(subcellPos, t);
                }
            }


        }

        private cellDef BuildCellDef(int index)
        {
            if (terrainByIndex == null || occupancyByIndex == null || index < 0 || index >= CellCount)
            {
                return new cellDef
                {
                    index = index,
                    placeability = CellPlaceability.Blocked,
                    isDisplayZone = false
                };
            }

            return cellDef.From(terrainByIndex[index], occupancyByIndex[index]);
        }

        private CellPlaceability ResolvePlaceability(TileBase tile)
        {
            if (tile == null) return nullCellPlaceability;

            string tileName = tile.name;
            return tileName switch
            {
                var s when s == "normalCell" => normalCellPlaceability,
                var s when s == "connectorCell" => connectorCellPlaceability,
                var s when s == "displayCell" => displayCellPlaceability,
                _ => nullCellPlaceability
            };
        }

        private void ResetCellPlaceability(int index)
        {
            if (terrainByIndex == null || basePlaceabilityByIndex == null) return;
            if (index < 0 || index >= terrainByIndex.Length) return;

            var terrain = terrainByIndex[index];
            var basePlaceability = basePlaceabilityByIndex[index];
            if (terrain.placeability == basePlaceability) return;

            terrain.placeability = basePlaceability;
            terrainByIndex[index] = terrain;
            RefreshCellHighlight(index);
        }

        private void cellDefByType()
        {
            nullCellPlaceability = CellPlaceability.Blocked;
            normalCellPlaceability = CellPlaceability.Placeable;
            connectorCellPlaceability = CellPlaceability.ConnectorsOnly;
            displayCellPlaceability = CellPlaceability.Display;
        }

        private void EnsureHighlightParent()
        {
            if (cellHighlightParent != null) return;
            var go = new GameObject("CellHighlights");
            go.transform.SetParent(transform, worldPositionStays: false);
            cellHighlightParent = go.transform;
        }

        private void EnsureSubGridHighlightParent()
        {
            if (subGridHighlightParent != null) return;
            var go = new GameObject("SubGridCellHighlights");
            go.transform.SetParent(transform, worldPositionStays: false);
            subGridHighlightParent = go.transform;
        }

        private void BuildCellHighlightPool()
        {
            if (!enableCellHighlights || cellHighlightSprite == null) return;

            EnsureHighlightParent();
            cellHighlights = new SpriteRenderer[CellCount];
            subGridHighlights = mirrorHighlightsToSubGrid ? new SpriteRenderer[CellCount] : null;
            if (subGridHighlights != null)
            {
                EnsureSubGridHighlightParent();
            }

            for (int i = 0; i < CellCount; i++)
            {
                var (x, y) = FromIndex(i);
                var mainName = $"cellHighlight_{x}_{y}";
                cellHighlights[i] = CreateCellHighlightRenderer(mainName, new Vector2Int(x, y), Vector3.zero, cellHighlightParent);

                if (subGridHighlights != null && y == 0)
                {
                    var subName = $"subGridCellHighlight_{x}_{y}";
                    subGridHighlights[i] = CreateCellHighlightRenderer(subName, new Vector2Int(x, y), subGridHighlightOffset, subGridHighlightParent);
                }
            }
        }

        private SpriteRenderer CreateCellHighlightRenderer(string name, Vector2Int cell, Vector3 offset, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.position = CellToWorld(cell) + offset;

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = cellHighlightSprite;
            renderer.sortingLayerName = cellHighlightSortingLayer;
            renderer.sortingOrder = cellHighlightSortingOrder;
            renderer.enabled = false;
            return renderer;
        }

        private void RefreshCellHighlights()
        {
            if (cellHighlights == null) return;

            for (int i = 0; i < cellHighlights.Length; i++)
            {
                RefreshCellHighlight(i);
            }
        }

        private void RefreshCellHighlight(int index)
        {
            if (!enableCellHighlights) return;
            if (cellHighlights == null || index < 0 || index >= cellHighlights.Length) return;

            var renderer = cellHighlights[index];
            var subGridRenderer = subGridHighlights != null && index < subGridHighlights.Length
                ? subGridHighlights[index]
                : null;
            if (renderer == null) return;

            var cell = BuildCellDef(index);
            bool hasComponent = cell.HasComponent;
            bool hasWire = cell.HasWire;
            bool hasPipe = cell.HasPipe;
            bool hasContent = hasComponent || hasWire || hasPipe;

            // Disable highlights for out-of-bounds or blocked cells without contents
            if (cell.placeability == CellPlaceability.Blocked && !hasContent)
            {
                renderer.enabled = false;
                if (subGridRenderer != null) subGridRenderer.enabled = false;
                return;
            }

            Color color;
            if (hasContent)
            {
                bool mixed = (hasComponent ? 1 : 0) + (hasWire ? 1 : 0) + (hasPipe ? 1 : 0) > 1;
                color = mixed
                    ? mixedContentColor
                    : hasComponent ? componentContentColor
                    : hasWire ? wireContentColor
                    : pipeContentColor;
            }
            else
            {
                color = cell.placeability switch
                {
                    CellPlaceability.Placeable => placeableColor,
                    CellPlaceability.ConnectorsOnly => connectorsOnlyColor,
                    CellPlaceability.Display => displayColor,
                    _ => blockedColor
                };
            }

            renderer.color = color;
            renderer.enabled = true;
            if (subGridRenderer != null)
            {
                subGridRenderer.color = color;
                subGridRenderer.enabled = true;
            }
        }

        private void UpdateCellPlaceability(int index, CellPlaceability placeability)
        {
            if (terrainByIndex == null || index < 0 || index >= terrainByIndex.Length) return;

            var terrain = terrainByIndex[index];
            if (terrain.placeability == placeability) return;

            terrain.placeability = placeability;
            terrainByIndex[index] = terrain;

            RefreshCellHighlight(index);
        }



        // --- Bounds & Indexing ---
        public bool InBounds(int x, int y) => (uint)x < (uint)width && (uint)y < (uint)height;
        public int ToIndex(Vector2Int c) => CellIndex.ToIndex(c.x, c.y, width);
        public (int x, int y) FromIndex(int index) => CellIndex.FromIndex(index, width);

        private static Vector2Int RotateOffset(Vector2Int offset, int rotationSteps)
        {
            return rotationSteps switch
            {
                1 => new Vector2Int(offset.y, -offset.x),
                2 => new Vector2Int(-offset.x, -offset.y),
                3 => new Vector2Int(-offset.y, offset.x),
                _ => offset
            };
        }

        // --- Cells ---
        public cellDef GetCell(Vector2Int c) => BuildCellDef(ToIndex(c));

        public Vector3 CellToWorld(Vector2Int c) //provide cells positioning to the game space
        {
            return new Vector3(c.x + 0.5f, c.y + 0.5f, 0f);
        }
        public Vector2Int WorldToCell(Vector3 worldPos) //provide cell from game space position
        {
            int x = Mathf.FloorToInt(worldPos.x);
            int z = Mathf.FloorToInt(worldPos.y);
            return new Vector2Int(x, z);
        }
        public bool TryGetCell(Vector2Int c, out cellDef cell) //checking if cell is real and in bounds
        {
            cell = default;
            if (!InBounds(c.x, c.y)) return false;
            cell = GetCell(c);
            return true;
        }

        public bool TryPlaceComponent(Vector2Int c, MachineComponent component, bool markTerrainConnectorsOnly = false, bool allowDisplayCell = false)
        {
            if (!InBounds(c.x, c.y)) return false;
            int i = ToIndex(c);
            var placeability = terrainByIndex[i].placeability;
            bool isDisplayCell = placeability == CellPlaceability.Display;
            if (placeability == CellPlaceability.Blocked
                || placeability == CellPlaceability.ConnectorsOnly
                || (isDisplayCell && !allowDisplayCell))
                return false;

            var occupancy = occupancyByIndex[i];
            if (occupancy.HasComponent) return false;

            occupancy.component = component;
            occupancyByIndex[i] = occupancy;

            if (markTerrainConnectorsOnly && !isDisplayCell)
            {
                UpdateCellPlaceability(i, CellPlaceability.ConnectorsOnly);
            }

            return true;
        }

        public bool RemoveComponent(Vector2Int c)
        {
            if (!InBounds(c.x, c.y)) return false;
            int i = ToIndex(c);
            var occupancy = occupancyByIndex[i];
            if (!occupancy.HasComponent) return false;

            occupancy.component?.DestroyPortMarkers();
            occupancy.component = null;
            occupancyByIndex[i] = occupancy;
            ResetCellPlaceability(i);
            RaiseGridTopologyChanged();
            return true;
        }

        public bool RemoveComponent(MachineComponent component)
        {
            if (component == null) return false;

            var footprintCells = GetFootprintCells(component);
            var placementCells = footprintCells.OccupiedCells;
            if (placementCells == null || placementCells.Count == 0)
            {
                placementCells = footprintCells.DisplayCells;
            }

            if (placementCells == null || placementCells.Count == 0) return false;

            HashSet<Vector2Int> seen = null;
            if (placementCells.Count > 1)
            {
                seen = new HashSet<Vector2Int>();
            }

            bool removedAny = false;
            for (int i = 0; i < placementCells.Count; i++)
            {
                var cell = placementCells[i];
                if (seen != null && !seen.Add(cell)) continue;
                if (!InBounds(cell.x, cell.y)) continue;

                int idx = ToIndex(cell);
                var occupancy = occupancyByIndex[idx];
                if (occupancy.component != component) continue;

                occupancy.component = null;
                occupancyByIndex[idx] = occupancy;
                ResetCellPlaceability(idx);
                removedAny = true;
            }

            if (!removedAny) return false;

            component.DestroyPortMarkers();
            if (Application.isPlaying)
                Destroy(component.gameObject);
            else
                DestroyImmediate(component.gameObject);
            RaiseGridTopologyChanged();

            return true;
        }

        public bool TryDeleteSelection(InputRouter.SelectionInfo selection)
        {
            if (inventory == null) inventory = FindFirstObjectByType<Inventory>();
            if (inventory == null) return false;
            if (!selection.hasSelection) return false;

            switch (selection.target)
            {
                case InputRouter.CellSelectionTarget.Component:
                    return TryDeleteComponent(selection.cellData.component);
                case InputRouter.CellSelectionTarget.Wire:
                    return TryDeleteWire(selection.cellData.GetWireAt(selection.wireIndex));
                case InputRouter.CellSelectionTarget.Pipe:
                    return TryDeletePipe(selection.cellData.GetPipeAt(selection.pipeIndex));
                default:
                    return false;
            }
        }

        private bool TryDeleteComponent(MachineComponent component)
        {
            if (component == null) return false;
            if (!RemoveComponent(component)) return false;

            ReturnComponentToInventory(component);
            return true;
        }

        private void ReturnComponentToInventory(MachineComponent component)
        {
            if (component?.def == null || inventory == null) return;
            inventory.AddItem(component.def.defName, 1);
        }

        private bool TryDeleteWire(PlacedWire wire)
        {
            if (wire == null) return false;
            if (!ClearWireRun(wire.occupiedCells, wire)) return false;

            Destroy(wire.gameObject);
            return true;
        }

        private bool TryDeletePipe(PlacedPipe pipe)
        {
            if (pipe == null) return false;
            if (!ClearPipeRun(pipe.occupiedCells, pipe)) return false;

            if (pipe.lineRenderer != null)
            {
                Destroy(pipe.lineRenderer.gameObject);
            }

            Destroy(pipe.gameObject);
            return true;
        }

        public bool AddWireRun(IEnumerable<Vector2Int> cells, PlacedWire wire)
        {
            if (cells == null || wire == null) return false;

            bool IsPortCell(Vector2Int cell, MachineComponent component)
            {
                if (component == null) return false;

                bool isStart = component == wire.startComponent && cell == wire.startPortCell;
                bool isEnd = component == wire.endComponent && cell == wire.endPortCell;

                return isStart || isEnd;
            }

            foreach (var c in cells)
            {
                if (!InBounds(c.x, c.y))
                {
                    Debug.LogWarning($"AddWireRun failed: cell {c} out of bounds.");
                    return false;
                }

                int idx = ToIndex(c);
                var terrain = terrainByIndex[idx];
                var occupancy = occupancyByIndex[idx];

                if (terrain.placeability == CellPlaceability.Blocked)
                {
                    Debug.LogWarning($"AddWireRun failed: cell {c} is blocked.");
                    return false;
                }

                if (occupancy.HasComponent && !IsPortCell(c, occupancy.component))
                {
                    Debug.LogWarning($"AddWireRun failed: cell {c} contains unrelated component '{occupancy.component.name}'.");
                    return false;
                }
            }

            bool placedAny = false;
            foreach (var c in cells)
            {
                int idx = ToIndex(c);
                var occupancy = occupancyByIndex[idx];
                occupancy.AddWire(wire);
                occupancyByIndex[idx] = occupancy;
                placedAny = true;
            }

            return placedAny;
        }

        public bool AddPipeRun(IEnumerable<Vector2Int> cells, PlacedPipe pipe)
        {
            if (cells == null || pipe == null) return false;

            bool IsPortCell(Vector2Int cell, MachineComponent component)
            {
                if (component == null) return false;

                bool isStart = component == pipe.startComponent && cell == pipe.startPortCell;
                bool isEnd = component == pipe.endComponent && cell == pipe.endPortCell;

                return isStart || isEnd;
            }

            bool IsEndpointComponent(MachineComponent component)
            {
                if (component == null) return false;

                return component == pipe.startComponent || component == pipe.endComponent;
            }

            foreach (var c in cells)
            {
                if (!InBounds(c.x, c.y))
                {
                    Debug.LogWarning($"AddPipeRun failed: cell {c} out of bounds.");
                    return false;
                }

                int idx = ToIndex(c);
                var terrain = terrainByIndex[idx];
                var occupancy = occupancyByIndex[idx];

                if (terrain.placeability == CellPlaceability.Blocked)
                {
                    Debug.LogWarning($"AddPipeRun failed: cell {c} is blocked.");
                    return false;
                }

                if (occupancy.HasComponent && !IsPortCell(c, occupancy.component) && !IsEndpointComponent(occupancy.component))
                {
                    Debug.LogWarning($"AddPipeRun failed: cell {c} contains unrelated component '{occupancy.component.name}'.");
                    return false;
                }
            }

            bool placedAny = false;
            foreach (var c in cells)
            {
                int idx = ToIndex(c);
                var occupancy = occupancyByIndex[idx];
                occupancy.AddPipe(pipe);
                occupancyByIndex[idx] = occupancy;
                placedAny = true;
            }

            if (placedAny)
            {
                RaiseGridTopologyChanged();
            }

            return placedAny;
        }

        public bool ClearWireRun(IEnumerable<Vector2Int> cells, PlacedWire wire)
        {
            if (cells == null || wire == null) return false;

            foreach (var c in cells)
            {
                if (!InBounds(c.x, c.y))
                {
                    Debug.LogWarning($"ClearWireRun failed: cell {c} out of bounds.");
                    return false;
                }
            }

            bool clearedAny = false;
            foreach (var c in cells)
            {
                int idx = ToIndex(c);
                var occupancy = occupancyByIndex[idx];

                if (occupancy.wires != null && occupancy.wires.Contains(wire))
                {
                    occupancy.wires.Remove(wire);
                    if (occupancy.wires.Count == 0)
                    {
                        occupancy.wires = null;
                    }

                    occupancyByIndex[idx] = occupancy;
                    clearedAny = true;
                }
            }

            return clearedAny;
        }

        public bool ClearPipeRun(IEnumerable<Vector2Int> cells, PlacedPipe pipe)
        {
            if (cells == null || pipe == null) return false;

            foreach (var c in cells)
            {
                if (!InBounds(c.x, c.y))
                {
                    Debug.LogWarning($"ClearPipeRun failed: cell {c} out of bounds.");
                    return false;
                }
            }

            bool clearedAny = false;
            foreach (var c in cells)
            {
                int idx = ToIndex(c);
                var occupancy = occupancyByIndex[idx];

                if (occupancy.pipes != null && occupancy.pipes.Contains(pipe))
                {
                    occupancy.pipes.Remove(pipe);
                    if (occupancy.pipes.Count == 0)
                    {
                        occupancy.pipes = null;
                    }

                    occupancyByIndex[idx] = occupancy;
                    clearedAny = true;
                }
            }

            if (clearedAny)
            {
                RaiseGridTopologyChanged();
            }

            return clearedAny;
        }

        public bool ClearCell(Vector2Int c)
        {
            if (!InBounds(c.x, c.y)) return false;
            int i = ToIndex(c);
            var occupancy = occupancyByIndex[i];
            occupancy.Clear();
            occupancyByIndex[i] = occupancy;
            RaiseGridTopologyChanged();
            return true;
        }

        // --- Spills ---
        public float GetSpill(Vector2Int c) => spillByIndex[ToIndex(c)];
        public void AddSpill(Vector2Int c, float amount)
        {
            int i = ToIndex(c);
            spillByIndex[i] = Mathf.Max(0f, spillByIndex[i] + amount);
        }
        public void SetSpill(Vector2Int c, float value)
        {
            spillByIndex[ToIndex(c)] = Mathf.Max(0f, value);
        }

        // --- Overlays ---
        public bool HasPower(Vector2Int c) => powerByIndex[ToIndex(c)];
        public bool HasWater(Vector2Int c) => waterByIndex[ToIndex(c)];
        public void SetPower(Vector2Int c, bool on) => powerByIndex[ToIndex(c)] = on;
        public void SetWater(Vector2Int c, bool on) => waterByIndex[ToIndex(c)] = on;

        // --- Things ---
        public IReadOnlyList<ThingDef> ThingsAt(Vector2Int c) => bucketByIndex[ToIndex(c)];
        //public Machine EdificeAt(Vector2Int c) => edificeByIndex[ToIndex(c)];

        /*
        public void AddThing(ThingDef t, Vector2Int c)
        {
            if (!InBounds(c.x, c.y)) return;
            int i = ToIndex(c);
            bucketByIndex[i].Add(t);
            t.cell = c;
        }
        

        public void RemoveThing(ThingDef t)
        {
            int i = ToIndex(t.cell);
            bucketByIndex[i].Remove(t);
        }
         */

        // --- Queries ---
        public bool IsPlaceable(Vector2Int c)
        {
            if (!InBounds(c.x, c.y)) return false;
            var terrain = terrainByIndex[ToIndex(c)];
            if (terrain.placeability == CellPlaceability.Blocked) return false;

            return true;
        }

        public int GetFillState(Vector2Int c)
        {
            if (!InBounds(c.x, c.y)) return 0;
            var terrain = terrainByIndex[ToIndex(c)];
            if (terrain.placeability == CellPlaceability.Blocked) return 0;

            return (int)terrain.placeability;
        }

        /// <summary>
        /// Creates sprite-based highlights for every occupied cell. Cells with components,
        /// wires, or pipes will receive a SpriteRenderer colored by the contents. Returns the
        /// created renderers so callers can manage their lifecycle (e.g., destroy or pool).
        /// </summary>
        /// <param name="highlightSprite">Sprite to render for each occupied cell.</param>
        /// <param name="parent">Transform used as the parent for all created highlights.</param>
        /// <param name="componentColor">Tint for cells containing a component.</param>
        /// <param name="wireColor">Tint for cells containing only wire.</param>
        /// <param name="pipeColor">Tint for cells containing only pipe.</param>
        /// <param name="mixedColor">Tint when multiple content types share the cell.</param>
        /// <param name="sortingLayer">Sorting layer used for the highlight renderers.</param>
        /// <param name="sortingOrder">Sorting order used for the highlight renderers.</param>
        public List<SpriteRenderer> CreateOccupancyHighlights(
            Sprite highlightSprite,
            Transform parent,
            Color componentColor,
            Color wireColor,
            Color pipeColor,
            Color mixedColor,
            string sortingLayer = "Default",
            int sortingOrder = 0)
        {
            var highlights = new List<SpriteRenderer>();
            if (highlightSprite == null || parent == null) return highlights;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var c = new Vector2Int(x, y);
                    var cell = GetCell(c);

                    bool hasComponent = cell.HasComponent;
                    bool hasWire = cell.HasWire;
                    bool hasPipe = cell.HasPipe;

                    if (!hasComponent && !hasWire && !hasPipe) continue;

                    Color color = hasComponent && hasWire || hasComponent && hasPipe || hasWire && hasPipe
                        ? mixedColor
                        : hasComponent ? componentColor
                        : hasWire ? wireColor
                        : pipeColor;

                    var go = new GameObject($"occupancyHighlight_{x}_{y}");
                    go.transform.SetParent(parent, worldPositionStays: false);
                    go.transform.position = CellToWorld(c);

                    var renderer = go.AddComponent<SpriteRenderer>();
                    renderer.sprite = highlightSprite;
                    renderer.color = color;
                    renderer.sortingLayerName = sortingLayer;
                    renderer.sortingOrder = sortingOrder;
                    highlights.Add(renderer);
                }
            }

            return highlights;
        }

        /*
        public bool IsWalkable(Vector2Int c) {
            if (!InBounds(c.x, c.z)) return false;
            var e = EdificeAt(c);
            if (e != null && e.def != null && e.def.passability == Passability.Impassable) return false;
            return true;
        }
        */

      
        #endregion

        
    }
}
        


