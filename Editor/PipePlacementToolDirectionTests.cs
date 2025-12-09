using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace MachineRepair.Tests
{
    public class PipePlacementToolDirectionTests
    {
        private MethodInfo isDirectionAllowedMethod;

        [SetUp]
        public void SetUp()
        {
            isDirectionAllowedMethod = typeof(PipePlacementTool).GetMethod(
                "IsDirectionAllowed",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(isDirectionAllowedMethod, "IsDirectionAllowed should exist for direction filtering");
        }

        [Test]
        public void FirstStep_AllDirectionsAllowedExceptZero()
        {
            Assert.IsTrue(InvokeIsDirectionAllowed(Vector2Int.zero, Vector2Int.left));
            Assert.IsTrue(InvokeIsDirectionAllowed(Vector2Int.zero, Vector2Int.one));
            Assert.IsFalse(InvokeIsDirectionAllowed(Vector2Int.zero, Vector2Int.zero));
        }

        [Test]
        public void ForwardAndAdjacentCellsRemainAllowed()
        {
            var currentDirection = Vector2Int.right;

            Assert.IsTrue(InvokeIsDirectionAllowed(currentDirection, Vector2Int.right),
                "Straight ahead should remain allowed for path continuation");

            Assert.IsTrue(InvokeIsDirectionAllowed(currentDirection, Vector2Int.up),
                "Cells adjacent to the forward cell that also touch the current cell should be allowed");
        }

        [Test]
        public void BacktrackingDirection_IsRejected()
        {
            var currentDirection = Vector2Int.up;

            Assert.IsFalse(InvokeIsDirectionAllowed(currentDirection, Vector2Int.down),
                "FindPathInternal should skip moves that backtrack directly behind the current heading");
        }

        [Test]
        public void WideZigZag_AwayFromForwardCell_IsRejected()
        {
            var currentDirection = new Vector2Int(1, 1);
            var wideTurn = new Vector2Int(-1, 1);

            Assert.IsFalse(InvokeIsDirectionAllowed(currentDirection, wideTurn),
                "Candidates not touching the straight-ahead cell should be filtered out of the path search");
        }

        [Test]
        public void SlightTurnAdjacentToForwardDiagonal_IsAllowed()
        {
            var currentDirection = new Vector2Int(1, 1);
            var slightTurn = new Vector2Int(1, 0);

            Assert.IsTrue(InvokeIsDirectionAllowed(currentDirection, slightTurn),
                "Adjacent neighbors around the straight-ahead diagonal cell should remain valid");
        }

        private bool InvokeIsDirectionAllowed(Vector2Int currentDirection, Vector2Int candidateDirection)
        {
            var tool = new PipePlacementTool();
            return (bool)isDirectionAllowedMethod.Invoke(tool, new object[] { currentDirection, candidateDirection });
        }
    }
}
