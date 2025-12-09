using System.Collections.Generic;
using MachineRepair.Grid;
using UnityEngine;

namespace MachineRepair
{
    /// <summary>
    /// Represents a wire laid on the grid, tracking its connections and basic
    /// simulation data.
    /// </summary>
    [RequireComponent(typeof(Transform))]
    public class PlacedWire : MonoBehaviour
    {
        [Header("Connections")]
        public WireType wireType;
        public WireDef wireDef;
        public MachineComponent startComponent;
        public MachineComponent endComponent;
        public Vector2Int startPortCell;
        public Vector2Int endPortCell;
        public List<Vector2Int> occupiedCells = new();

        [Header("State")]
        public bool wireDamaged;
        public float voltage;
        public float current;
        public float resistance;

        [Header("Visuals")]
        [SerializeField] private float bloomIntensity = 3f;
        [SerializeField] private LineRenderer lineRenderer;

        private Color baseColor = Color.white;
        private bool isCircuitPowered;

        /// <summary>
        /// Checks whether the wire should be marked as damaged based on current and
        /// resistance thresholds.
        /// </summary>
        public bool EvaluateDamage(float maxCurrent, float maxResistance)
        {
            bool overloaded = current > maxCurrent && resistance > maxResistance;
            wireDamaged |= overloaded;
            return wireDamaged;
        }

        public void AttachRenderer(LineRenderer renderer, Color color)
        {
            lineRenderer = renderer;
            if (lineRenderer != null && lineRenderer.material != null)
            {
                lineRenderer.material = new Material(lineRenderer.material);
            }
            baseColor = color;
            ApplyBloom(false);
        }

        public void SetCircuitPowered(bool powered)
        {
            if (isCircuitPowered == powered) return;
            isCircuitPowered = powered;
            ApplyBloom(isCircuitPowered);
        }

        private void ApplyBloom(bool enabled)
        {
            if (lineRenderer == null) return;

            var targetColor = enabled ? baseColor * bloomIntensity : baseColor;
            targetColor.a = baseColor.a;

            lineRenderer.startColor = targetColor;
            lineRenderer.endColor = targetColor;

            var material = lineRenderer.material;
            if (material != null && material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", enabled ? targetColor : Color.black);
            }
        }
    }
}
