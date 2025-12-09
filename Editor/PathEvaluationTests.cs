using MachineRepair.Pathing;
using NUnit.Framework;
using UnityEngine;

namespace MachineRepair.Tests
{
    public class PathEvaluationTests
    {
        [Test]
        public void ZeroPreviousDirection_AllowsInitialStep()
        {
            Assert.IsTrue(PathEvaluation.IsTurnAllowed(Vector2Int.zero, Vector2Int.right));
        }

        [Test]
        public void RightAngleTurn_IsAllowed()
        {
            Assert.IsTrue(PathEvaluation.IsTurnAllowed(Vector2Int.up, Vector2Int.right));
        }

        [Test]
        public void AcuteDiagonalTurn_IsAllowed()
        {
            Assert.IsTrue(PathEvaluation.IsTurnAllowed(new Vector2Int(1, 1), Vector2Int.right));
        }

        [Test]
        public void ZeroDirection_IsRejected()
        {
            Assert.IsFalse(PathEvaluation.IsTurnAllowed(Vector2Int.up, Vector2Int.zero));
        }
    }
}
