using System;
using System.Collections.Generic;
using UnityEngine;

namespace MachineRepair{
    [CreateAssetMenu(fileName = "ThingDef", menuName = "EspressoGrid/ThingDef")]
    public sealed class ThingDef : ScriptableObject {
        public string defName;
        public string displayName;
        [TextArea(2,6)] public string description;

        [Header("Component Semantics")]
         public ComponentType type = ComponentType.None;

        [Header("Footprint (in cells)")]
        public FootprintMask footprint;

        [Header("Connection Ports")]
        public PortDef connectionPorts;

        [Header("Inventory")]
        public int maxStack = 16;

        [Header("Ratings / Sim Params (examples)")]
        public bool requiresPower;
        public bool passthroughPower = true; // change on broken part
        public bool passthroughWater = true; //change for valve status
        public float maxPressure = 12f; //Bar
        public float maxACVoltage = 240f; //AC voltage
        public float maxDCVoltage = 24f; //DC voltage
        public float wattage;
        public float flowCoef = 1f;
        public float volumeL = 0.5f;
        public float heatRateW = 1800f;
        public float temperatureC = 20f;
        public float targetTempMin = 92f;
        public float targetTempMax = 96f;


        [Header("Sprites")]
        public Sprite icon;   // shown in Inventory UI + Inspect panel
        public Sprite sprite;      // shown when placed on grid (ComponentInstance)
        public Sprite displaySprite; // shown on subgrid display cells
        [Header("Visual Tweaks for placed sprite")]
        public float placedSpriteScale = 1f;
        public int placedSortingOrder = 200;
    }

    [Serializable]
    public struct FootprintMask
    {
        public int width;
        public int height;
        public Vector2Int origin;
        public bool[] occupied;
        public bool[] display;

        public int IndexOf(Vector2Int p) => p.y * width + p.x;
        public bool Occupies(Vector2Int p) => occupied[IndexOf(p)];
        public bool Displays(Vector2Int p) => display[IndexOf(p)];
    }
}

