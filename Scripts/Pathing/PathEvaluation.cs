using UnityEngine;

namespace MachineRepair.Pathing
{
    /// <summary>
    /// Utility helpers for evaluating connector path candidates.
    /// Keeps turn validation in one place so wire and pipe tools stay consistent.
    /// </summary>
    public static class PathEvaluation
    {
        private static readonly Vector2Int[] Directions =
        {
            Vector2Int.up,
            new(1, 1),
            Vector2Int.right,
            new(1, -1),
            Vector2Int.down,
            new(-1, -1),
            Vector2Int.left,
            new(-1, 1)
        };

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

        /// <summary>
        /// Attempts to build a two-step diagonal sequence that keeps the minimum turn angle intact
        /// while eventually realigning to the desired direction. This is used when a direct 90-degree
        /// corner would be rejected by <paramref name="minTurnAngleDegrees"/>. A successful sequence
        /// returns diagonals representing a pair of 135-degree turns (clockwise or counter-clockwise)
        /// relative to the previous direction.
        /// </summary>
        public static bool TryBuildWideTurn(
            Vector2Int previousDirection,
            Vector2Int desiredDirection,
            float minTurnAngleDegrees,
            out Vector2Int firstDiagonal,
            out Vector2Int secondDiagonal)
        {
            firstDiagonal = Vector2Int.zero;
            secondDiagonal = Vector2Int.zero;

            if (previousDirection == Vector2Int.zero || desiredDirection == Vector2Int.zero) return false;
            if (IsTurnAllowed(previousDirection, desiredDirection, minTurnAngleDegrees)) return false;

            int previousIndex = DirectionToIndex(previousDirection);
            int desiredIndex = DirectionToIndex(desiredDirection);
            if (previousIndex < 0 || desiredIndex < 0) return false;

            int delta = (desiredIndex - previousIndex + Directions.Length) % Directions.Length;
            int turnStep = 0;
            if (delta == 2) turnStep = -3; // 90 degrees clockwise => two 135-degree counter-clockwise turns
            else if (delta == 6) turnStep = 3; // 90 degrees counter-clockwise => two 135-degree clockwise turns
            else return false;

            int firstIndex = (previousIndex + turnStep + Directions.Length) % Directions.Length;
            int secondIndex = (previousIndex + 2 * turnStep + Directions.Length) % Directions.Length;
            if (secondIndex != desiredIndex) return false;

            firstDiagonal = Directions[firstIndex];
            secondDiagonal = Directions[secondIndex];

            // Double-check that each leg respects the angle requirement.
            return IsTurnAllowed(previousDirection, firstDiagonal, minTurnAngleDegrees)
                   && IsTurnAllowed(firstDiagonal, secondDiagonal, minTurnAngleDegrees);
        }

        private static int DirectionToIndex(Vector2Int direction)
        {
            for (int i = 0; i < Directions.Length; i++)
            {
                if (Directions[i] == direction) return i;
            }

            return -1;
        }
    }
}
