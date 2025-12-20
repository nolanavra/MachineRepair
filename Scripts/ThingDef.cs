using System;
using UnityEngine;

namespace MachineRepair{
    [CreateAssetMenu(fileName = "ThingDef", menuName = "EspressoGrid/ThingDef")]
    public sealed class ThingDef : ScriptableObject {
        [Header("Identity")]
        public string defName;
        public string displayName;
        [TextArea(2,6)] public string description;

        [Header("Component Semantics")]
        public ComponentType componentType = ComponentType.None;

        [Header("Footprint (in cells)")]
        public FootprintMask footprintMask;

        [Header("Capabilities")]
        public bool water = false;
        public bool power = false;
        public bool signals = false;

        [Header("Water Properties (required when water = true)")]
        public float maxPressure = 12f; //Bar
        public float volume = 0.5f;
        public float fillLevel = 0f;

        [Header("Power Properties (required when power = true)")]
        public float wattage;
        public float voltage;
        public bool AC = true;

        [Header("Signal Properties (required when signals = true)")]
        public bool Rx;
        public bool Tx;
        public bool CompPortInteraction;

        [Header("Inventory")]
        public int maxStack = 16; // additional authoring data for inventory interactions

        [Header("Simulation / Authoring Extras")]
        [Tooltip("Legacy power passthrough toggle retained for simulation prototyping.")]
        public bool passthroughPower = true; // change on broken part
        [Tooltip("Legacy water passthrough toggle retained for valve simulation prototyping.")]
        public bool passthroughWater = true; //change for valve status
        [Tooltip("Legacy flow coefficient retained for existing water propagation logic.")]
        public float flowCoef = 1f;
        [Tooltip("Legacy volume (liters) retained for future thermal/pressure work.")]
        public float volumeL = 0.5f;
        [Tooltip("Legacy heat rate retained for thermal simulation hooks.")]
        public float heatRateW = 1800f;
        [Tooltip("Legacy temperature retained for thermal simulation hooks.")]
        public float temperatureC = 20f;
        [Tooltip("Legacy target temperature min retained for thermal simulation hooks.")]
        public float targetTempMin = 92f;
        [Tooltip("Legacy target temperature max retained for thermal simulation hooks.")]
        public float targetTempMax = 96f;


        [Header("Sprites")]
        public Sprite icon;   // shown in Inventory UI + Inspect panel
        public Sprite sprite;      // shown when placed on grid (ComponentInstance)
        public Sprite displaySprite; // shown on subgrid display cells
        [Tooltip("Fine tune the display sprite position relative to the subgrid cell (in world units).")]
        public float displayTrimX = 0f;
        public float displayTrimY = 0f;
        [Header("Visual Tweaks for placed sprite")]
        [Tooltip("When true, automatically scale the placed sprite to the ThingDef footprint; when false, keep the prefab/local scale and only apply placedSpriteScale.")]
        public bool constrainPlacedSpriteToFootprint = true;
        [Tooltip("When true, display sprites are sized to the footprint bounds; when false, keep the authored sprite scale.")]
        public bool constrainDisplaySpriteToFootprint = true;
        public float placedSpriteScale = 1f;
        public int placedSortingOrder = 200;

        [Obsolete("Use footprintMask instead. Footprint data must be ThingDef-authored.")]
        public FootprintMask footprint
        {
            get => footprintMask;
            set => footprintMask = value;
        }

        [Obsolete("Use footprintMask.connectedPorts instead. Ports are authored on the footprint mask.")]
        public PortDef connectionPorts
        {
            get => footprintMask.connectedPorts;
            set => footprintMask.connectedPorts = value;
        }

        private void OnValidate()
        {
            ValidateCapabilities();
            footprintMask.EnsureInitialized();

            if (!footprintMask.IsValid(out var error))
            {
                Debug.LogError($"ThingDef {name} has invalid footprint: {error}", this);
            }

            if (footprintMask.connectedPorts == null)
            {
                Debug.LogWarning($"ThingDef {name} is missing connectedPorts on footprintMask. Ports must be authored on the ThingDef footprint.", this);
            }
        }

        private void ValidateCapabilities()
        {
            if (water)
            {
                if (maxPressure <= 0f) Debug.LogError($"ThingDef {name} has water=true but maxPressure is not set.", this);
                if (volume < 0f) Debug.LogError($"ThingDef {name} has water=true but volume is negative.", this);
            }

            if (power)
            {
                if (wattage < 0f) Debug.LogError($"ThingDef {name} has power=true but wattage is negative.", this);
                if (voltage <= 0f) Debug.LogError($"ThingDef {name} has power=true but voltage is not set.", this);
            }

            if (signals && !Rx && !Tx && !CompPortInteraction)
            {
                Debug.LogWarning($"ThingDef {name} has signals enabled but no signal capabilities specified.", this);
            }
        }
    }

    [Serializable]
    public struct FootprintMask
    {
        public int width;
        public int height;
        public Vector2Int origin;
        public bool[] occupied;
        public bool[] display;
        public PortDef connectedPorts;

        public int IndexOf(Vector2Int p) => p.y * width + p.x;
        public bool Occupies(Vector2Int p) => occupied[IndexOf(p)];
        public bool Displays(Vector2Int p) => display[IndexOf(p)];

        public int CellCount => Mathf.Max(0, width) * Mathf.Max(0, height);

        public void EnsureInitialized()
        {
            int cells = CellCount;
            if (cells <= 0) return;

            if (occupied == null || occupied.Length != cells)
            {
                occupied = new bool[cells];
            }

            if (display == null || display.Length != cells)
            {
                display = new bool[cells];
            }
        }

        public bool IsValid(out string error)
        {
            int cells = CellCount;
            if (width <= 0 || height <= 0)
            {
                error = "Footprint width and height must be positive.";
                return false;
            }

            if (occupied == null || occupied.Length != cells)
            {
                error = "Footprint occupied mask must be initialized and match width*height.";
                return false;
            }

            if (display == null || display.Length != cells)
            {
                error = "Footprint display mask must be initialized and match width*height.";
                return false;
            }

            if (origin.x < 0 || origin.x >= width || origin.y < 0 || origin.y >= height)
            {
                error = "Origin must be within footprint bounds.";
                return false;
            }

            if (connectedPorts == null)
            {
                error = "Footprint connectedPorts must be assigned.";
                return false;
            }

            if (connectedPorts.ports == null)
            {
                error = "connectedPorts.ports must be initialized (can be empty).";
                return false;
            }

            for (int i = 0; i < connectedPorts.ports.Length; i++)
            {
                var port = connectedPorts.ports[i];
                if (port.internalConnectionIndices == null)
                {
                    error = $"Port {i} is missing internalConnectionIndices.";
                    return false;
                }

                if (!IsWithinBounds(port.cell))
                {
                    error = $"Port {i} cell {port.cell} is outside footprint bounds.";
                    return false;
                }

                if (port.portType == PortType.Water && port.flowrateMax <= 0f)
                {
                    error = $"Port {i} has water type but flowrateMax is not set.";
                    return false;
                }

                for (int c = 0; c < port.internalConnectionIndices.Length; c++)
                {
                    int idx = port.internalConnectionIndices[c];
                    if (idx < 0 || idx >= connectedPorts.ports.Length)
                    {
                        error = $"Port {i} has invalid internal connection index {idx}.";
                        return false;
                    }

                    if (idx == i)
                    {
                        error = $"Port {i} has a self-referential internal connection.";
                        return false;
                    }
                }
            }

            error = string.Empty;
            return true;
        }

        private bool IsWithinBounds(Vector2Int p)
        {
            return p.x >= 0 && p.x < width && p.y >= 0 && p.y < height;
        }
    }
}

