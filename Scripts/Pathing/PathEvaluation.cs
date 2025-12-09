using UnityEngine;

namespace MachineRepair.Pathing
{
    /// <summary>
    /// Utility helpers for evaluating connector path candidates.
    /// Keeps turn validation in one place so wire and pipe tools stay consistent.
    /// </summary>
    public static class PathEvaluation
    {
        /// <summary>
        /// Determines whether the proposed direction is valid. Any non-zero direction is accepted.
        /// </summary>
        public static bool IsTurnAllowed(Vector2Int previousDirection, Vector2Int newDirection)
        {
            _ = previousDirection; // kept for API compatibility with existing callers
            if (newDirection == Vector2Int.zero) return false;
            return true;
        }
    }
}
