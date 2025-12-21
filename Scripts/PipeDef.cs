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

        [Header("Curves")]
        [Min(0f)] public float cornerRadius = 0.25f;
        [Min(0)] public int samplesPerCorner = 2;
        [Min(0)] public int lineCornerVertices = 1;
        [Min(0)] public int lineCapVertices = 1;

        [Header("Semantics")]
        [Min(0f)] public float maxPressure = 10f;
        [Min(0f)] public float maxFlow = 1f;

        [Header("Hydraulics (SI)")]
        [Min(0.0001f)] public float innerDiameter_m = 0.01f;
        [Min(0f)] public float roughness_m = 1e-5f;
        [Tooltip("Minor loss coefficients (K values) summed into the Darcy–Weisbach evaluation.")]
        public float[] minorLosses = System.Array.Empty<float>();
        [Min(0f)] public float fluidDensity_kgm3 = 998f;
        [Min(0f)] public float fluidViscosity_PaS = 1.002e-3f;
    }
}
