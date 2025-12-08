using UnityEngine;

namespace MachineRepair
{
    [CreateAssetMenu(menuName = "Espresso/Pipe Definition")]
    public class PipeDef : ScriptableObject
    {
        [Header("Display")]
        public string displayName = "Pipe";
        public Color pipeColor = new Color(0.75f, 0.55f, 1f, 1f);
        [Min(0f)] public float lineWidth = 0.07f;

        [Header("Semantics")]
        [Min(0f)] public float maxPressure = 10f;
        [Min(0f)] public float maxFlow = 1f;
    }
}
