using System;

namespace MachineRepair.Fluid
{
    /// <summary>
    /// Deterministic, allocation-free solver for incompressible pipe flow using Darcy–Weisbach.
    /// Provides helpers to solve volumetric flow rate from a pressure drop and to evaluate the model forward.
    /// All units are SI:
    /// ΔP [Pa], L and D [m], Q [m^3/s], ρ [kg/m^3], μ [Pa·s], ε [m].
    /// </summary>
    public static class PipeFlowSolver
    {
        private const double ReynoldsLaminarUpper = 2300.0;
        private const double ReynoldsTransitionalUpper = 4000.0;
        private const double MinLogArgument = 1e-12;
        private const double DefaultBracketGrowth = 2.0;
        private const double MaxBracketFlow = 1e6;

        /// <summary>
        /// Solve for volumetric flow rate (Q) from a pressure drop (ΔP) using Darcy–Weisbach with optional minor losses.
        /// Returns true on success and false if parameters are invalid or no bracket could be found.
        /// </summary>
        public static bool TrySolveQFromDeltaP(
            double deltaP_Pa,
            double length_m,
            double diameter_m,
            double roughness_m,
            double[] minorLossCoefficients,
            double density_kgm3,
            double dynamicViscosity_Pa_s,
            double relTol,
            double absTol,
            int maxSolveIterations,
            double qMaxHint,
            out double q_m3s)
        {
            q_m3s = 0d;

            if (double.IsNaN(deltaP_Pa) ||
                double.IsNaN(length_m) ||
                double.IsNaN(diameter_m) ||
                double.IsNaN(roughness_m) ||
                double.IsNaN(density_kgm3) ||
                double.IsNaN(dynamicViscosity_Pa_s) ||
                relTol <= 0d ||
                absTol < 0d ||
                maxSolveIterations <= 0)
            {
                return false;
            }

            if (deltaP_Pa < 0d)
            {
                return false;
            }

            if (deltaP_Pa == 0d)
            {
                q_m3s = 0d;
                return true;
            }

            if (length_m <= 0d || diameter_m <= 0d || density_kgm3 <= 0d || dynamicViscosity_Pa_s <= 0d)
            {
                return false;
            }

            double minorLossSum = SumMinorLosses(minorLossCoefficients);
            double bracketLow = 0d;
            double bracketHigh = qMaxHint > 0d ? qMaxHint : 1e-6;

            double dpHigh = EvaluateDeltaP(bracketHigh, length_m, diameter_m, roughness_m, density_kgm3, dynamicViscosity_Pa_s, minorLossSum);
            if (double.IsNaN(dpHigh))
            {
                return false;
            }

            int guard = 0;
            while (dpHigh < deltaP_Pa)
            {
                bracketLow = bracketHigh;
                bracketHigh *= DefaultBracketGrowth;
                guard++;

                if (bracketHigh > MaxBracketFlow || guard > 128)
                {
                    return false;
                }

                dpHigh = EvaluateDeltaP(bracketHigh, length_m, diameter_m, roughness_m, density_kgm3, dynamicViscosity_Pa_s, minorLossSum);
                if (double.IsNaN(dpHigh))
                {
                    return false;
                }

                if (dpHigh <= 0d && bracketHigh >= MaxBracketFlow)
                {
                    return false;
                }
            }

            double bracketMid = 0d;
            double dpMid = 0d;
            double desiredDp = deltaP_Pa;
            double flowTolerance;

            for (int i = 0; i < maxSolveIterations; i++)
            {
                bracketMid = 0.5 * (bracketLow + bracketHigh);
                dpMid = EvaluateDeltaP(bracketMid, length_m, diameter_m, roughness_m, density_kgm3, dynamicViscosity_Pa_s, minorLossSum);
                if (double.IsNaN(dpMid))
                {
                    return false;
                }

                double dpError = Math.Abs(dpMid - desiredDp);
                double allowedDpError = Math.Max(absTol, relTol * Math.Abs(desiredDp));
                flowTolerance = Math.Max(absTol, relTol * Math.Max(Math.Abs(bracketHigh), Math.Abs(bracketLow)));

                if (dpError <= allowedDpError || (0.5 * (bracketHigh - bracketLow)) <= flowTolerance)
                {
                    q_m3s = bracketMid;
                    return true;
                }

                if (dpMid > desiredDp)
                {
                    bracketHigh = bracketMid;
                }
                else
                {
                    bracketLow = bracketMid;
                }
            }

            q_m3s = bracketMid;
            flowTolerance = Math.Max(absTol, relTol * Math.Max(Math.Abs(bracketHigh), Math.Abs(bracketLow)));
            return Math.Abs(dpMid - desiredDp) <= Math.Max(absTol, relTol * Math.Abs(desiredDp)) ||
                   (0.5 * (bracketHigh - bracketLow)) <= flowTolerance;
        }

        /// <summary>
        /// Convenience overload preconfigured for 20°C water (ρ=998 kg/m^3, μ=1.002e-3 Pa·s).
        /// </summary>
        public static bool TrySolveQFromDeltaP_Water20C(
            double deltaP_Pa,
            double length_m,
            double diameter_m,
            double roughness_m,
            double[] minorLossCoefficients,
            double relTol,
            double absTol,
            int maxSolveIterations,
            double qMaxHint,
            out double q_m3s)
        {
            const double rhoWater = 998.0;
            const double muWater = 1.002e-3;

            return TrySolveQFromDeltaP(
                deltaP_Pa,
                length_m,
                diameter_m,
                roughness_m,
                minorLossCoefficients,
                rhoWater,
                muWater,
                relTol,
                absTol,
                maxSolveIterations,
                qMaxHint,
                out q_m3s);
        }

        /// <summary>
        /// Forward evaluation of the Darcy–Weisbach + minor-loss model.
        /// </summary>
        public static double DeltaP_Model(
            double q_m3s,
            double length_m,
            double diameter_m,
            double roughness_m,
            double density_kgm3,
            double dynamicViscosity_Pa_s,
            double[] minorLossCoefficients)
        {
            double minorLossSum = SumMinorLosses(minorLossCoefficients);
            return EvaluateDeltaP(q_m3s, length_m, diameter_m, roughness_m, density_kgm3, dynamicViscosity_Pa_s, minorLossSum);
        }

        /// <summary>
        /// Darcy friction factor using laminar, blended transitional, and Swamee–Jain turbulent regimes.
        /// </summary>
        public static double FrictionFactor_Darcy(double reynoldsNumber, double roughness_m, double diameter_m)
        {
            if (diameter_m <= 0d || roughness_m < 0d || reynoldsNumber <= 0d || double.IsNaN(reynoldsNumber))
            {
                return double.NaN;
            }

            if (reynoldsNumber < ReynoldsLaminarUpper)
            {
                return 64.0 / reynoldsNumber;
            }

            double turbulent = ComputeSwameeJain(reynoldsNumber, roughness_m, diameter_m);
            if (reynoldsNumber >= ReynoldsTransitionalUpper)
            {
                return turbulent;
            }

            double laminar = 64.0 / reynoldsNumber;
            double t = (reynoldsNumber - ReynoldsLaminarUpper) / (ReynoldsTransitionalUpper - ReynoldsLaminarUpper);
            return ((1d - t) * laminar) + (t * turbulent);
        }

        private static double SumMinorLosses(double[] minorLossCoefficients)
        {
            if (minorLossCoefficients == null)
            {
                return 0d;
            }

            double sum = 0d;
            for (int i = 0; i < minorLossCoefficients.Length; i++)
            {
                double k = minorLossCoefficients[i];
                if (double.IsNaN(k))
                {
                    continue;
                }

                if (k > 0d)
                {
                    sum += k;
                }
            }

            return sum;
        }

        private static double EvaluateDeltaP(
            double q_m3s,
            double length_m,
            double diameter_m,
            double roughness_m,
            double density_kgm3,
            double dynamicViscosity_Pa_s,
            double minorLossSum)
        {
            if (q_m3s <= 0d)
            {
                return 0d;
            }

            if (length_m <= 0d || diameter_m <= 0d || density_kgm3 <= 0d || dynamicViscosity_Pa_s <= 0d)
            {
                return double.NaN;
            }

            double area = Math.PI * diameter_m * diameter_m * 0.25d;
            if (area <= 0d)
            {
                return double.NaN;
            }

            double velocity = q_m3s / area;
            double reynolds = density_kgm3 * velocity * diameter_m / dynamicViscosity_Pa_s;
            if (reynolds <= 0d || double.IsNaN(reynolds))
            {
                return double.NaN;
            }

            double frictionFactor = FrictionFactor_Darcy(reynolds, roughness_m, diameter_m);
            if (double.IsNaN(frictionFactor))
            {
                return double.NaN;
            }

            double majorTerm = frictionFactor * (length_m / diameter_m);
            double lossCoefficient = majorTerm + minorLossSum;
            if (lossCoefficient < 0d)
            {
                lossCoefficient = 0d;
            }

            return lossCoefficient * (density_kgm3 * velocity * velocity * 0.5d);
        }

        private static double ComputeSwameeJain(double reynoldsNumber, double roughness_m, double diameter_m)
        {
            double roughnessTerm = roughness_m / (3.7 * diameter_m);
            double reynoldsTerm = 5.74 / Math.Pow(reynoldsNumber, 0.9);
            double argument = roughnessTerm + reynoldsTerm;
            if (argument < MinLogArgument)
            {
                argument = MinLogArgument;
            }
            else if (Math.Abs(argument - 1d) < MinLogArgument)
            {
                argument += MinLogArgument;
            }

            double denominator = Math.Log10(argument);
            return 0.25 / (denominator * denominator);
        }
    }
}
