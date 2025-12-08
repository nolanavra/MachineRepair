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

        [Header("Cells")]
        private CellPlaceability nullCellPlaceability;
        private CellPlaceability normalCellPlaceability;
        private CellPlaceability connectorCellPlaceability;
        private CellPlaceability displayCellPlaceability;
        private CellTerrain[] terrainByIndex;
        private CellOccupancy[] occupancyByIndex;
        public cellDef[] cellSubGrid;


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
        [SerializeField] private GameObject componentPrefab;

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

        private SpriteRenderer[] cellHighlights;

        private void Start()
        {

        }
        void Awake()
        {
            cellDefByType();
            InitGrids();
            setup = true;

            EnsureHighlightParent();
            BuildCellHighlightPool();

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
                terrainByIndex[i] = new CellTerrain
                {
                    index = i,
                    placeability = placeability
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

                var footprintCells = GetFootprintCells(gridCell, def.footprint);
                if (!IsFootprintPlaceable(footprintCells)) continue;

                var component = CreateComponentInstance(def, gridCell);
                if (TryPlaceComponentFootprint(component, footprintCells))
                {
                    ApplyPortMarkers(component);
                }
            }
        }

        private bool TryPlaceComponentFootprint(MachineComponent component, IReadOnlyList<Vector2Int> cells)
        {
            if (component == null || cells == null || cells.Count == 0) return false;

            for (int i = 0; i < cells.Count; i++)
            {
                if (!TryPlaceComponent(cells[i], component, markTerrainConnectorsOnly: true))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsFootprintPlaceable(IReadOnlyList<Vector2Int> cells)
        {
            if (cells == null || cells.Count == 0) return false;

            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                if (!InBounds(c.x, c.y)) return false;

                int idx = ToIndex(c);
                var terrain = terrainByIndex[idx];
                if (terrain.placeability == CellPlaceability.Blocked || terrain.placeability == CellPlaceability.ConnectorsOnly)
                    return false;

                if (occupancyByIndex[idx].HasComponent)
                    return false;
            }

            return true;
        }

        private List<Vector2Int> GetFootprintCells(Vector2Int anchorCell, FootprintMask footprint)
        {
            var cells = new List<Vector2Int>();

            for (int y = 0; y < footprint.height; y++)
            {
                for (int x = 0; x < footprint.width; x++)
                {
                    if (!footprint.occupied[y * footprint.width + x]) continue;

                    Vector2Int local = new Vector2Int(x - footprint.origin.x, y - footprint.origin.y);
                    cells.Add(anchorCell + local);
                }
            }

            return cells;
        }

        private List<Vector2Int> GetFootprintCells(MachineComponent component)
        {
            var cells = new List<Vector2Int>();
            if (component == null || component.footprint.occupied == null) return cells;

            FootprintMask footprint = component.footprint;
            for (int y = 0; y < footprint.height; y++)
            {
                for (int x = 0; x < footprint.width; x++)
                {
                    if (!footprint.occupied[y * footprint.width + x]) continue;

                    Vector2Int local = new Vector2Int(x - footprint.origin.x, y - footprint.origin.y);
                    Vector2Int rotated = RotateOffset(local, component.rotation);
                    cells.Add(component.anchorCell + rotated);
                }
            }

            return cells;
        }

        private MachineComponent CreateComponentInstance(ThingDef def, Vector2Int anchorCell)
        {
            GameObject instance = componentPrefab != null
                ? Instantiate(componentPrefab)
                : new GameObject(def.displayName ?? def.defName ?? "MachineComponent");

            instance.name = def.displayName ?? def.defName ?? instance.name;

            var machine = instance.GetComponent<MachineComponent>();
            if (machine == null)
                machine = instance.AddComponent<MachineComponent>();

            machine.def = def;
            machine.grid = this;
            machine.footprint = def.footprint;
            machine.rotation = 0;
            machine.anchorCell = anchorCell;
            machine.portDef = def.connectionPorts;

            instance.transform.position = CellToWorld(anchorCell);
            instance.transform.rotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one * def.placedSpriteScale;

            var spriteRenderer = instance.GetComponent<SpriteRenderer>();
            if (spriteRenderer == null)
                spriteRenderer = instance.AddComponent<SpriteRenderer>();

            spriteRenderer.sprite = def.sprite;
            spriteRenderer.color = Color.white;
            spriteRenderer.sortingOrder = def.placedSortingOrder;

            ApplyPortMarkers(machine);

            return machine;
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
                    placeability = CellPlaceability.Blocked
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

        private void BuildCellHighlightPool()
        {
            if (!enableCellHighlights || cellHighlightSprite == null) return;

            EnsureHighlightParent();
            cellHighlights = new SpriteRenderer[CellCount];

            for (int i = 0; i < CellCount; i++)
            {
                var (x, y) = FromIndex(i);
                var go = new GameObject($"cellHighlight_{x}_{y}");
                go.transform.SetParent(cellHighlightParent, worldPositionStays: false);
                go.transform.position = CellToWorld(new Vector2Int(x, y));

                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = cellHighlightSprite;
                renderer.sortingLayerName = cellHighlightSortingLayer;
                renderer.sortingOrder = cellHighlightSortingOrder;
                renderer.enabled = false;
                cellHighlights[i] = renderer;
            }
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

        public bool TryPlaceComponent(Vector2Int c, MachineComponent component, bool markTerrainConnectorsOnly = false)
        {
            if (!InBounds(c.x, c.y)) return false;
            int i = ToIndex(c);
            var placeability = terrainByIndex[i].placeability;
            if (placeability == CellPlaceability.Blocked || placeability == CellPlaceability.ConnectorsOnly) return false;

            var occupancy = occupancyByIndex[i];
            if (occupancy.HasComponent) return false;

            occupancy.component = component;
            occupancyByIndex[i] = occupancy;

            if (markTerrainConnectorsOnly)
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
            return true;
        }

        public bool RemoveComponent(MachineComponent component)
        {
            if (component == null) return false;

            var footprintCells = GetFootprintCells(component);
            if (footprintCells.Count == 0) return false;

            bool removedAny = false;
            for (int i = 0; i < footprintCells.Count; i++)
            {
                var cell = footprintCells[i];
                if (!InBounds(cell.x, cell.y)) continue;

                int idx = ToIndex(cell);
                var occupancy = occupancyByIndex[idx];
                if (occupancy.component != component) continue;

                occupancy.component = null;
                occupancyByIndex[idx] = occupancy;
                removedAny = true;
            }

            if (!removedAny) return false;

            component.DestroyPortMarkers();
            if (Application.isPlaying)
                Destroy(component.gameObject);
            else
                DestroyImmediate(component.gameObject);

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

                if (occupancy.HasComponent && !IsPortCell(c, occupancy.component))
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

            return placedAny;
        }

        public bool ClearCell(Vector2Int c)
        {
            if (!InBounds(c.x, c.y)) return false;
            int i = ToIndex(c);
            var occupancy = occupancyByIndex[i];
            occupancy.Clear();
            occupancyByIndex[i] = occupancy;
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
        


