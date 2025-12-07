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

        public Vector2Int GetGlobalCell(Vector2Int localCell)
        {
            return anchorCell + RotateOffset(localCell, rotation);
        }

        public Vector2Int GetGlobalCell(PortLocal port)
        {
            Vector2Int footprintOrigin = footprint.origin;
            return port.ToGlobalCell(anchorCell, rotation, footprintOrigin);
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

        private void OnDestroy()
        {
            DestroyPortMarkers();
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

