using System.Collections.Generic;
using MachineRepair.Grid;
using UnityEngine;

namespace MachineRepair
{
    /// <summary>
    /// Represents a pipe laid on the grid, tracking its connections and
    /// pressure/flow metadata.
    /// </summary>
    [RequireComponent(typeof(Transform))]
    public class PlacedPipe : MonoBehaviour
    {
        [Header("Definition")]
        public PipeDef pipeDef;

        [Header("Connections")]
        public MachineComponent startComponent;
        public MachineComponent endComponent;
        public Vector2Int startPortCell;
        public Vector2Int endPortCell;
        public List<Vector2Int> occupiedCells = new();

        [Header("State")]
        public float pressure;
        public float flow;
        [Tooltip("0-100% fill of the pipe run. Updated by the water simulation.")]
        [Range(0f, 100f)] public float fillLevel;
        [Tooltip("Maximum flowrate supported by this pipe run (copied from PipeDef.maxFlow).")]
        public float flowrateMax;
        public bool pipeDamaged;

        [Header("Visuals")]
        public LineRenderer lineRenderer;
    }
}
