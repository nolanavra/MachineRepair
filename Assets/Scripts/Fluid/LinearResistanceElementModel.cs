using System;

namespace MachineRepair.Fluid
{
    /// <summary>
    /// Simple linear ΔP = R * Q element with optional flow cap. Useful for internal connectors.
    /// </summary>
    public sealed class LinearResistanceElementModel : IHydraulicElementModel
    {
        private readonly double resistance_PaPer_m3s;
        private readonly double maxFlow_m3s;
        private double lastDeltaP_Pa;

        public LinearResistanceElementModel(double resistance_PaPer_m3s, double maxFlow_m3s = 0d)
        {
            this.resistance_PaPer_m3s = Math.Max(1e-9, resistance_PaPer_m3s);
            this.maxFlow_m3s = Math.Max(0d, maxFlow_m3s);
        }

        public double LastDeltaP => lastDeltaP_Pa;

        public double EvaluateDeltaP(double flow_m3s)
        {
            lastDeltaP_Pa = flow_m3s * resistance_PaPer_m3s;
            return lastDeltaP_Pa;
        }

        public double EstimateDerivative(double flow_m3s, double deltaQ)
        {
            return resistance_PaPer_m3s;
        }

        public double SolveFlow(double deltaP_Pa, HydraulicSolverSettings settings)
        {
            double flow = deltaP_Pa / resistance_PaPer_m3s;
            if (maxFlow_m3s > 0d)
            {
                flow = Clamp(flow, -maxFlow_m3s, maxFlow_m3s);
            }

            EvaluateDeltaP(flow);
            return flow;
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
