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
    }
}
