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
            Assert.IsTrue(PathEvaluation.IsTurnAllowed(Vector2Int.zero, Vector2Int.right, 120f));
        }

        [Test]
        public void RightAngleTurn_IsRejected()
        {
            Assert.IsFalse(PathEvaluation.IsTurnAllowed(Vector2Int.up, Vector2Int.right, 120f));
        }

        [Test]
        public void ObtuseDiagonalTurn_IsAllowed()
        {
            Assert.IsTrue(PathEvaluation.IsTurnAllowed(Vector2Int.right, new Vector2Int(-1, 1), 120f));
        }

        [Test]
        public void AcuteDiagonalZigZag_IsRejected()
        {
            Assert.IsFalse(PathEvaluation.IsTurnAllowed(new Vector2Int(1, 1), Vector2Int.right, 120f));
        }
    }
}
