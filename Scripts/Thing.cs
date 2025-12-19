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
        FlowRestrictor,
        Switch
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

        [Header("Simulation State")]
        [SerializeField] private bool isPowered;

        [Header("Connections")]
        [SerializeField] private List<MachineComponent> powerConnections = new();
        [SerializeField] private List<MachineComponent> waterConnections = new();
        [SerializeField] private List<MachineComponent> signalConnections = new();

        public IReadOnlyList<MachineComponent> PowerConnections => powerConnections;
        public IReadOnlyList<MachineComponent> WaterConnections => waterConnections;
        public IReadOnlyList<MachineComponent> SignalConnections => signalConnections;

        public bool IsPowered => isPowered;

        public Vector2Int GetGlobalCell(Vector2Int localCell)
        {
            return anchorCell + RotateOffset(localCell, rotation);
        }

        public Vector2Int GetGlobalCell(PortLocal port)
        {
            Vector2Int footprintOrigin = footprint.origin;
            return port.ToGlobalCell(anchorCell, rotation, footprintOrigin);
        }

        public void SetPowered(bool powered)
        {
            isPowered = powered;
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
                    renderer.color = ResolvePortColor(port.portType, powerColor, waterColor, signalColor);
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
            int sortingOrder,
            FootprintMask footprintMask,
            Vector2Int anchor,
            int rotationSteps)
        {
            if (displaySprites == null) displaySprites = new List<SpriteRenderer>();

            int activeCount = 0;
            if (owningGrid != null && sprite != null && displayCells != null)
            {
                EnsureDisplaySpriteParent();

                bool hasValidDisplayCell = false;
                if (displayCells != null)
                {
                    for (int i = 0; i < displayCells.Count; i++)
                    {
                        var cell = displayCells[i];
                        if (owningGrid.InBounds(cell.x, cell.y))
                        {
                            hasValidDisplayCell = true;
                            break;
                        }
                    }
                }

                if (hasValidDisplayCell && TryGetFootprintBounds(footprintMask, anchor, rotationSteps, out var min, out var max))
                {
                    float width = (max.x - min.x) + 1;
                    float height = (max.y - min.y) + 1;

                    Vector3 targetSize = new Vector3(width, height, 1f);
                    Vector2 spriteSize = sprite.bounds.size;
                    Vector3 scale = new Vector3(
                        spriteSize.x != 0 ? targetSize.x / spriteSize.x : 1f,
                        spriteSize.y != 0 ? targetSize.y / spriteSize.y : 1f,
                        1f);

                    Vector3 center = new Vector3(
                        min.x + (width * 0.5f),
                        min.y + (height * 0.5f),
                        0f);
                    var renderer = EnsureDisplaySprite(activeCount, sprite, sortingLayer, sortingOrder);
                    renderer.transform.position = center + subGridOffset;
                    renderer.transform.localScale = scale;
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

        private static bool TryGetFootprintBounds(
            FootprintMask footprintMask,
            Vector2Int anchor,
            int rotationSteps,
            out Vector2Int min,
            out Vector2Int max)
        {
            min = default;
            max = default;

            if (footprintMask.width <= 0 || footprintMask.height <= 0)
            {
                return false;
            }

            Vector2Int localMin = new Vector2Int(-footprintMask.origin.x, -footprintMask.origin.y);
            Vector2Int localMax = new Vector2Int(
                footprintMask.width - footprintMask.origin.x - 1,
                footprintMask.height - footprintMask.origin.y - 1);

            Vector2Int[] corners = new[]
            {
                localMin,
                new Vector2Int(localMin.x, localMax.y),
                new Vector2Int(localMax.x, localMin.y),
                localMax
            };

            int normalizedRotation = ((rotationSteps % 4) + 4) % 4;
            Vector2Int rotatedMin = new Vector2Int(int.MaxValue, int.MaxValue);
            Vector2Int rotatedMax = new Vector2Int(int.MinValue, int.MinValue);

            for (int i = 0; i < corners.Length; i++)
            {
                var rotated = RotateOffset(corners[i], normalizedRotation);
                if (rotated.x < rotatedMin.x) rotatedMin.x = rotated.x;
                if (rotated.y < rotatedMin.y) rotatedMin.y = rotated.y;
                if (rotated.x > rotatedMax.x) rotatedMax.x = rotated.x;
                if (rotated.y > rotatedMax.y) rotatedMax.y = rotated.y;
            }

            min = rotatedMin + anchor;
            max = rotatedMax + anchor;
            return true;
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
