using System;

namespace MachineRepair.Fluid
{
    /// <summary>
    /// Darcy–Weisbach pipe element with optional minor losses. All units are SI.
    /// ΔP [Pa], L [m], D [m], Q [m^3/s], ρ [kg/m^3], μ [Pa·s], ε [m].
    /// </summary>
    public sealed class PipeElementModel : IHydraulicElementModel
    {
        private readonly double length_m;
        private readonly double diameter_m;
        private readonly double roughness_m;
        private readonly double[] minorLossCoefficients;
        private readonly double density_kgm3;
        private readonly double dynamicViscosity_Pa_s;
        private readonly double flowLimit_m3s;
        private double lastDeltaP_Pa;

        public PipeElementModel(
            double length_m,
            double diameter_m,
            double roughness_m,
            double[] minorLossCoefficients,
            double density_kgm3,
            double dynamicViscosity_Pa_s,
            double flowLimit_m3s = 0d)
        {
            this.length_m = Math.Max(1e-6, length_m);
            this.diameter_m = Math.Max(1e-6, diameter_m);
            this.roughness_m = Math.Max(0d, roughness_m);
            this.minorLossCoefficients = minorLossCoefficients;
            this.density_kgm3 = Math.Max(1e-6, density_kgm3);
            this.dynamicViscosity_Pa_s = Math.Max(1e-12, dynamicViscosity_Pa_s);
            if (double.IsNaN(flowLimit_m3s) || double.IsInfinity(flowLimit_m3s))
            {
                flowLimit_m3s = 0d;
            }
            this.flowLimit_m3s = Math.Max(0d, flowLimit_m3s);
        }

        public double LastDeltaP => lastDeltaP_Pa;

        public double SolveFlow(double deltaP_Pa, HydraulicSolverSettings settings)
        {
            double sign = Math.Sign(deltaP_Pa);
            double targetDeltaP = Math.Abs(deltaP_Pa);
            if (targetDeltaP <= settings.AbsoluteTolerance)
            {
                lastDeltaP_Pa = deltaP_Pa;
                return 0d;
            }

            double qHint = flowLimit_m3s > 0d ? flowLimit_m3s : 0.01d;
            if (!PipeFlowSolver.TrySolveQFromDeltaP(
                    targetDeltaP,
                    length_m,
                    diameter_m,
                    roughness_m,
                    minorLossCoefficients,
                    density_kgm3,
                    dynamicViscosity_Pa_s,
                    settings.RelativeTolerance,
                    settings.AbsoluteTolerance,
                    settings.MaxEdgeSolveIterations,
                    qHint,
                    out var flow_m3s))
            {
                double fallback = EvaluateDeltaP(sign * qHint);
                lastDeltaP_Pa = fallback;
                return 0d;
            }

            double signedFlow = sign * flow_m3s;
            if (flowLimit_m3s > 0d)
            {
                signedFlow = Clamp(signedFlow, -flowLimit_m3s, flowLimit_m3s);
            }

            lastDeltaP_Pa = EvaluateDeltaP(signedFlow);
            return signedFlow;
        }

        public double EvaluateDeltaP(double flow_m3s)
        {
            double magnitude = PipeFlowSolver.DeltaP_Model(
                Math.Abs(flow_m3s),
                length_m,
                diameter_m,
                roughness_m,
                density_kgm3,
                dynamicViscosity_Pa_s,
                minorLossCoefficients);

            double signed = Math.Sign(flow_m3s) * magnitude;
            lastDeltaP_Pa = signed;
            return signed;
        }

        public double EstimateDerivative(double flow_m3s, double deltaQ)
        {
            double step = Math.Max(1e-12, Math.Abs(deltaQ));
            double cached = lastDeltaP_Pa;
            double forward = EvaluateDeltaP(flow_m3s + step);
            double backward = EvaluateDeltaP(flow_m3s - step);
            lastDeltaP_Pa = cached;
            return (forward - backward) / (2d * step);
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
