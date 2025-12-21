using System.Collections.Generic;
using MachineRepair.Fluid;
using NUnit.Framework;
using UnityEngine;

namespace MachineRepair.Tests.Fluid
{
    public class HydraulicNetworkSolverTests
    {
        [Test]
        public void SinglePipeHonorsFixedPressuresAndDirection()
        {
            var nodes = new List<HydraulicNode>
            {
                new HydraulicNode { Id = 0, Cell = Vector2Int.zero, IsFixedPressure = true, FixedPressure_Pa = 200_000d, Pressure_Pa = 200_000d },
                new HydraulicNode { Id = 1, Cell = Vector2Int.right, IsFixedPressure = true, FixedPressure_Pa = 100_000d, Pressure_Pa = 100_000d }
            };

            var pipe = new PipeElementModel(
                length_m: 2d,
                diameter_m: 0.02d,
                roughness_m: 1e-5d,
                minorLossCoefficients: null,
                density_kgm3: 998d,
                dynamicViscosity_Pa_s: 1.002e-3d);

            var edges = new List<HydraulicEdge>
            {
                new HydraulicEdge
                {
                    Id = 0,
                    NodeA = 0,
                    NodeB = 1,
                    Model = pipe,
                    Kind = HydraulicEdgeKind.Pipe
                }
            };

            var solver = new HydraulicNetworkSolver();
            var settings = HydraulicSolverSettings.Default;
            var result = solver.Solve(nodes, edges, settings);

            Assert.IsTrue(result.Converged);
            Assert.That(edges[0].Flow_m3s, Is.GreaterThan(0d), "Flow should move from high to low pressure (A→B).");
            Assert.That(edges[0].LastDeltaP_Pa, Is.Positive);
        }

        [Test]
        public void SeriesResistancesReachExpectedPressures()
        {
            var nodes = new List<HydraulicNode>
            {
                new HydraulicNode { Id = 0, Cell = Vector2Int.zero, Injection_m3s = 1d, Pressure_Pa = 0d },
                new HydraulicNode { Id = 1, Cell = Vector2Int.right, Pressure_Pa = 0d },
                new HydraulicNode { Id = 2, Cell = new Vector2Int(2, 0), IsFixedPressure = true, FixedPressure_Pa = 0d, Pressure_Pa = 0d }
            };

            var edges = new List<HydraulicEdge>
            {
                new HydraulicEdge { Id = 0, NodeA = 0, NodeB = 1, Model = new LinearResistanceElementModel(5d), Kind = HydraulicEdgeKind.InternalConnector },
                new HydraulicEdge { Id = 1, NodeA = 1, NodeB = 2, Model = new LinearResistanceElementModel(5d), Kind = HydraulicEdgeKind.InternalConnector }
            };

            var solver = new HydraulicNetworkSolver();
            var settings = HydraulicSolverSettings.Default;
            settings.MaxNetworkIterations = 128;

            var result = solver.Solve(nodes, edges, settings);

            Assert.IsTrue(result.Converged, "Series network should converge.");
            Assert.That(nodes[0].Pressure_Pa, Is.EqualTo(10d).Within(0.05d), "Upstream node should hold sum of drops.");
            Assert.That(nodes[1].Pressure_Pa, Is.EqualTo(5d).Within(0.05d), "Middle node should show single drop.");
            Assert.That(edges[0].Flow_m3s, Is.EqualTo(1d).Within(0.01d));
            Assert.That(edges[1].Flow_m3s, Is.EqualTo(1d).Within(0.01d));
        }

        [Test]
        public void ParallelResistancesSplitFlowDeterministically()
        {
            var nodes = new List<HydraulicNode>
            {
                new HydraulicNode { Id = 0, Cell = Vector2Int.zero, Injection_m3s = 2d, Pressure_Pa = 0d },
                new HydraulicNode { Id = 1, Cell = Vector2Int.right, IsFixedPressure = true, FixedPressure_Pa = 0d, Pressure_Pa = 0d }
            };

            var edges = new List<HydraulicEdge>
            {
                new HydraulicEdge { Id = 0, NodeA = 0, NodeB = 1, Model = new LinearResistanceElementModel(10d), Kind = HydraulicEdgeKind.InternalConnector },
                new HydraulicEdge { Id = 1, NodeA = 0, NodeB = 1, Model = new LinearResistanceElementModel(20d), Kind = HydraulicEdgeKind.InternalConnector }
            };

            var solver = new HydraulicNetworkSolver();
            var settings = HydraulicSolverSettings.Default;
            settings.MaxNetworkIterations = 128;

            var result = solver.Solve(nodes, edges, settings);

            Assert.IsTrue(result.Converged);
            Assert.That(edges[0].Flow_m3s, Is.EqualTo(1.333d).Within(0.01d));
            Assert.That(edges[1].Flow_m3s, Is.EqualTo(0.667d).Within(0.01d));
            Assert.That(nodes[0].Pressure_Pa, Is.EqualTo(13.333d).Within(0.05d));
        }
    }
}
