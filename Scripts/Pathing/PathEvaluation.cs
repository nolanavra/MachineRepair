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
        /// Determines whether the proposed direction respects the minimum turn angle.
        /// Straight and zero-length directions are considered allowed.
        /// </summary>
        public static bool IsTurnAllowed(Vector2Int previousDirection, Vector2Int newDirection, float minTurnAngleDegrees)
        {
            if (newDirection == Vector2Int.zero) return false;
            if (previousDirection == Vector2Int.zero) return true;

            float angle = Vector2.Angle(previousDirection, newDirection);
            return !(angle > 0f && angle < minTurnAngleDegrees);
        }
    }
}
