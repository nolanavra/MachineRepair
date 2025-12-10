using MachineRepair.Grid;
using System.Collections.Generic;
using UnityEngine;

namespace MachineRepair {
    public enum ComponentType{
        None,
        ChassisPowerConnection,
        ChassisWaterConnection,
        Boiler,
        Pump,
        Grouphead,
        Controler,
        SolonoidValve,
        FlowRestrictor
    }

    [RequireComponent(typeof(Transform))]
    public class MachineComponent : MonoBehaviour {
        [Header("References")]
        [SerializeField] public ThingDef def;
        [SerializeField] public GridManager grid;
        public FootprintMask footprint;
        public int rotation;
        public Vector2Int anchorCell;
        public PortDef portDef;

        [Header("Runtime")]
        [SerializeField] private Transform portMarkerParent;
        [SerializeField] private List<SpriteRenderer> portMarkers = new();
        [SerializeField] private Transform displaySpriteParent;
        [SerializeField] private List<SpriteRenderer> displaySprites = new();

        [Header("Connections")]
        [SerializeField] private List<MachineComponent> powerConnections = new();
        [SerializeField] private List<MachineComponent> waterConnections = new();
        [SerializeField] private List<MachineComponent> signalConnections = new();

        public IReadOnlyList<MachineComponent> PowerConnections => powerConnections;
        public IReadOnlyList<MachineComponent> WaterConnections => waterConnections;
        public IReadOnlyList<MachineComponent> SignalConnections => signalConnections;

        public Vector2Int GetGlobalCell(Vector2Int localCell)
        {
            return anchorCell + RotateOffset(localCell, rotation);
        }

        public Vector2Int GetGlobalCell(PortLocal port)
        {
            Vector2Int footprintOrigin = footprint.origin;
            return port.ToGlobalCell(anchorCell, rotation, footprintOrigin);
        }

        public void RegisterConnection(PortType portType, MachineComponent other)
        {
            if (other == null || other == this) return;

            switch (portType)
            {
                case PortType.Power:
                    AddUnique(powerConnections, other);
                    break;
                case PortType.Water:
                    AddUnique(waterConnections, other);
                    break;
                case PortType.Signal:
                    AddUnique(signalConnections, other);
                    break;
            }
        }

        public void ClearConnections()
        {
            powerConnections.Clear();
            waterConnections.Clear();
            signalConnections.Clear();
        }

        public void RefreshPortMarkers(
            GridManager owningGrid,
            Sprite markerSprite,
            string sortingLayer,
            int sortingOrder,
            Color powerColor,
            Color waterColor,
            Color signalColor)
        {
            if (markerSprite == null || owningGrid == null)
            {
                return;
            }

            EnsurePortMarkerParent();

            int activeCount = 0;
            if (portDef != null && portDef.ports != null)
            {
                for (int i = 0; i < portDef.ports.Length; i++)
                {
                    var port = portDef.ports[i];
                    Vector2Int cell = GetGlobalCell(port);
                    if (!owningGrid.InBounds(cell.x, cell.y))
                        continue;

                    var renderer = EnsurePortMarker(activeCount, markerSprite, sortingLayer, sortingOrder);
                    renderer.color = ResolvePortColor(port.port, powerColor, waterColor, signalColor);
                    renderer.transform.position = owningGrid.CellToWorld(cell);
                    renderer.transform.rotation = transform.rotation;
                    renderer.gameObject.SetActive(true);
                    activeCount++;
                }
            }

            for (int i = activeCount; i < portMarkers.Count; i++)
            {
                if (portMarkers[i] != null)
                    portMarkers[i].gameObject.SetActive(false);
            }
        }

        public void DestroyPortMarkers()
        {
            for (int i = 0; i < portMarkers.Count; i++)
            {
                if (portMarkers[i] != null)
                {
                    var target = portMarkers[i].gameObject;
                    if (Application.isPlaying)
                        Object.Destroy(target);
                    else
                        Object.DestroyImmediate(target);
                }
            }

            portMarkers.Clear();
        }

        public void RefreshDisplaySprites(
            GridManager owningGrid,
            Sprite sprite,
            IReadOnlyList<Vector2Int> displayCells,
            Vector3 subGridOffset,
            string sortingLayer,
            int sortingOrder)
        {
            if (displaySprites == null) displaySprites = new List<SpriteRenderer>();

            int activeCount = 0;
            if (owningGrid != null && sprite != null && displayCells != null)
            {
                EnsureDisplaySpriteParent();

                Vector2Int? firstValidCell = null;
                Vector3 accumulatedWorld = Vector3.zero;
                int validCellCount = 0;
                for (int i = 0; i < displayCells.Count; i++)
                {
                    var cell = displayCells[i];
                    if (!owningGrid.InBounds(cell.x, cell.y)) continue;

                    Vector3 world = owningGrid.CellToWorld(cell);
                    accumulatedWorld += world;
                    validCellCount++;

                    if (!firstValidCell.HasValue)
                    {
                        firstValidCell = cell;
                    }
                }

                if (firstValidCell.HasValue && validCellCount > 0)
                {
                    Vector3 center = accumulatedWorld / validCellCount;
                    var renderer = EnsureDisplaySprite(activeCount, sprite, sortingLayer, sortingOrder);
                    renderer.transform.position = center + subGridOffset;
                    renderer.transform.rotation = Quaternion.identity;
                    renderer.gameObject.SetActive(true);
                    activeCount = 1;
                }
            }

            for (int i = activeCount; i < displaySprites.Count; i++)
            {
                if (displaySprites[i] != null)
                {
                    displaySprites[i].gameObject.SetActive(false);
                }
            }
        }

        public void DestroyDisplaySprites()
        {
            for (int i = 0; i < displaySprites.Count; i++)
            {
                if (displaySprites[i] != null)
                {
                    var target = displaySprites[i].gameObject;
                    if (Application.isPlaying)
                        Object.Destroy(target);
                    else
                        Object.DestroyImmediate(target);
                }
            }

            displaySprites.Clear();
        }

        private void OnDestroy()
        {
            DestroyPortMarkers();
            DestroyDisplaySprites();
        }

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

        private void EnsurePortMarkerParent()
        {
            if (portMarkerParent == null)
            {
                var go = new GameObject("portMarkers");
                go.transform.SetParent(transform, worldPositionStays: true);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                portMarkerParent = go.transform;
            }
        }

        private SpriteRenderer EnsurePortMarker(int index, Sprite sprite, string sortingLayer, int sortingOrder)
        {
            while (portMarkers.Count <= index)
            {
                var go = new GameObject("portMarker");
                go.transform.SetParent(portMarkerParent, worldPositionStays: true);
                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sortingLayerName = sortingLayer;
                renderer.sortingOrder = sortingOrder;
                renderer.sprite = sprite;
                portMarkers.Add(renderer);
            }

            var result = portMarkers[index];
            result.sortingLayerName = sortingLayer;
            result.sortingOrder = sortingOrder;
            result.sprite = sprite;
            return result;
        }

        private void EnsureDisplaySpriteParent()
        {
            if (displaySpriteParent == null)
            {
                var go = new GameObject("displaySprites");
                go.transform.SetParent(transform, worldPositionStays: true);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                displaySpriteParent = go.transform;
            }
        }

        private SpriteRenderer EnsureDisplaySprite(int index, Sprite sprite, string sortingLayer, int sortingOrder)
        {
            while (displaySprites.Count <= index)
            {
                var go = new GameObject("displaySprite");
                go.transform.SetParent(displaySpriteParent, worldPositionStays: true);
                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sortingLayerName = sortingLayer;
                renderer.sortingOrder = sortingOrder;
                renderer.sprite = sprite;
                displaySprites.Add(renderer);
            }

            var result = displaySprites[index];
            result.sortingLayerName = sortingLayer;
            result.sortingOrder = sortingOrder;
            result.sprite = sprite;
            return result;
        }

        private static void AddUnique(List<MachineComponent> list, MachineComponent component)
        {
            if (!list.Contains(component))
            {
                list.Add(component);
            }
        }

        private static Color ResolvePortColor(PortType port, Color powerColor, Color waterColor, Color signalColor)
        {
            return port switch
            {
                PortType.Power => powerColor,
                PortType.Water => waterColor,
                PortType.Signal => signalColor,
                _ => Color.white
            };
        }
    }

}

