using System;
using System.Collections.Generic;

namespace MachineRepair.Fluid
{
    /// <summary>
    /// Performs damped pressure iterations over a hydraulic network. Flows are solved per-edge,
    /// then nodal residuals drive bounded pressure updates. Designed to be allocation-free per tick.
    /// </summary>
    public sealed class HydraulicNetworkSolver
    {
        private double[] residuals = Array.Empty<double>();
        private double[] workingPressures = Array.Empty<double>();
        private double[] conductanceSums = Array.Empty<double>();

        public HydraulicSolveResult Solve(
            IList<HydraulicNode> nodes,
            IList<HydraulicEdge> edges,
            HydraulicSolverSettings settings)
        {
            if (nodes == null || edges == null)
            {
                return default;
            }

            EnsureBufferSize(nodes.Count);
            SeedPressures(nodes);

            int iterations = 0;
            double maxResidual = double.PositiveInfinity;

            for (; iterations < settings.MaxNetworkIterations; iterations++)
            {
                Array.Clear(residuals, 0, residuals.Length);
                Array.Clear(conductanceSums, 0, conductanceSums.Length);

                AccumulateEdgeFlows(nodes, edges, settings);
                AccumulateInjections(nodes);

                maxResidual = 0d;
                for (int i = 0; i < residuals.Length; i++)
                {
                    double mag = Math.Abs(residuals[i]);
                    if (mag > maxResidual)
                    {
                        maxResidual = mag;
                    }
                }

                if (maxResidual <= settings.ResidualTolerance)
                {
                    break;
                }

                UpdatePressures(nodes, settings);
            }

            CommitPressures(nodes);
            CommitEdges(edges);

            return new HydraulicSolveResult
            {
                Converged = maxResidual <= settings.ResidualTolerance,
                Iterations = iterations + 1,
                MaxResidual = maxResidual
            };
        }

        private void AccumulateEdgeFlows(
            IList<HydraulicNode> nodes,
            IList<HydraulicEdge> edges,
            HydraulicSolverSettings settings)
        {
            for (int i = 0; i < edges.Count; i++)
            {
                var edge = edges[i];
                int a = edge.NodeA;
                int b = edge.NodeB;
                if (a < 0 || a >= nodes.Count || b < 0 || b >= nodes.Count)
                {
                    continue;
                }

                double pa = workingPressures[a];
                double pb = workingPressures[b];
                double deltaP = pa - pb;

                double flow = edge.Model?.SolveFlow(deltaP, settings) ?? 0d;
                double derivative = edge.Model?.EstimateDerivative(flow, settings.DerivativeFlowStep) ?? 0d;
                double conductance = derivative > 1e-12 ? 1d / derivative : 0d;

                edge.Flow_m3s = flow;
                edge.LastDeltaP_Pa = edge.Model?.LastDeltaP ?? deltaP;
                edges[i] = edge;

                residuals[a] -= flow;
                residuals[b] += flow;

                conductanceSums[a] += conductance;
                conductanceSums[b] += conductance;
            }
        }

        private void AccumulateInjections(IList<HydraulicNode> nodes)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (Math.Abs(node.Injection_m3s) > double.Epsilon)
                {
                    residuals[i] += node.Injection_m3s;
                }
            }
        }

        private void UpdatePressures(IList<HydraulicNode> nodes, HydraulicSolverSettings settings)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (node.IsFixedPressure)
                {
                    workingPressures[i] = node.FixedPressure_Pa;
                    continue;
                }

                double conductance = conductanceSums[i];
                double scaledResidual = residuals[i] / Math.Max(1d, conductance);
                double update = settings.PressureRelaxation * scaledResidual;
                update = Clamp(update, -settings.MaxPressureStep, settings.MaxPressureStep);

                workingPressures[i] = Math.Max(settings.MinimumPressure, workingPressures[i] + update);
            }
        }

        private void SeedPressures(IList<HydraulicNode> nodes)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                workingPressures[i] = node.IsFixedPressure ? node.FixedPressure_Pa : Math.Max(node.Pressure_Pa, 0d);
            }
        }

        private void CommitPressures(IList<HydraulicNode> nodes)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                node.Pressure_Pa = workingPressures[i];
                nodes[i] = node;
            }
        }

        private static void CommitEdges(IList<HydraulicEdge> edges)
        {
            // Edges are already updated by reference; this exists for symmetry and clarity.
        }

        private void EnsureBufferSize(int nodeCount)
        {
            if (residuals.Length != nodeCount)
            {
                residuals = new double[nodeCount];
                workingPressures = new double[nodeCount];
                conductanceSums = new double[nodeCount];
            }
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
