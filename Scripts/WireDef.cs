using UnityEngine;

namespace MachineRepair
{
    [CreateAssetMenu(menuName = "Espresso/Wire Definition")]
    public class WireDef : ScriptableObject
    {
        [Header("Display")]
        public string displayName;
        public Color wireColor = Color.cyan;

        [Header("Semantics")]
        public WireType wireType = WireType.AC;
        [Min(0f)] public float maxCurrent = 10f;
    }
}
