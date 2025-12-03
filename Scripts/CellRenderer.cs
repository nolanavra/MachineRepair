using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

#region API: TILEMAPS
namespace MachineRepair.Grid.Tilemaps
{
    /// Paints the ShopGrid floors onto a Tilemap using FloorDef.tileSprite.
    [RequireComponent(typeof(GridManager))]

    public class TilemapCellPainter : MonoBehaviour
    {
        [Header("Refs")]
        public GridManager grid;
        public Tilemap cellTilemap;    // Create: GameObject "Floor", add Tilemap + TilemapRenderer
        public CellPlaceability placeability;

        [Header("Sorting")]
        public string sortingLayerName = "Default";
        public int sortingOrder = 0;

        void Reset()
        {
            var g = GetComponent<GridManager>();
            //if (g != null) g.cellSize = Vector3.one; // 1x1 cells
        }

        void Awake()
        {
            if (cellTilemap != null)
            {
                var r = cellTilemap.GetComponent<TilemapRenderer>();
                if (r != null)
                {
                    r.sortingLayerName = sortingLayerName;
                    r.sortingOrder = sortingOrder;
                }
            }
        }

        /*
        /// Paint the entire grid to the tilemap
        public void PaintAll()
        {
            if (grid == null || cellTilemap == null) { Debug.LogError("[TilemapCellPainter] Missing refs."); return; }

            cellTilemap.ClearAllTiles();

            for (int y = 0; y < grid.height; y++)
                for (int x = 0; x < grid.width; x++)
                {
                    var c = new Vector2Int(x, y);
                    var cell = grid.GetCell(c);
                    var sprite = cell ? cell.tileSprite : null;
                    var tile = TileCache.ForSprite(sprite);

                    if (tile == null) {cell.placeability = CellPlaceability.Blocked; continue; }
                    cellTilemap.SetTile(new Vector3Int(x, y, 0), tile);
                }
            
        }   


        /// Repaint a single cell, e.g., after changing a cell.
        public void RepaintCell(Vector2Int c)
        {
            if (grid == null || cellTilemap == null) return;
            if (!grid.InBounds(c.x, c.y)) return;

            var cell = grid.GetCell(c);
            var sprite = cell ? cell.tileSprite : null;
            var tile = TileCache.ForSprite(sprite);

            cellTilemap.SetTile(new Vector3Int(c.x, c.y, 0), tile);
        }
    }

        //-------------------------------------------------------------
        /// Caches runtime Tile instances keyed by Sprite so we don't allocate lots of duplicate Tiles.
        public static class TileCache
    {
        private static readonly Dictionary<Sprite, TileBase> _bySprite = new();

        public static TileBase ForSprite(Sprite sprite)
        {
            if (sprite == null) return null;
            if (_bySprite.TryGetValue(sprite, out var tile)) return tile;

            var t = ScriptableObject.CreateInstance<Tile>();
            t.sprite = sprite;
            _bySprite[sprite] = t;
            return t;
        }
        */
    }

    
}
#endregion
