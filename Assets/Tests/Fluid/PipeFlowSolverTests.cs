using MachineRepair.Fluid;
using NUnit.Framework;

namespace MachineRepair.Tests.Fluid
{
    public class PipeFlowSolverTests
    {
        [Test]
        public void ZeroDeltaP_ReturnsZeroFlow()
        {
            bool solved = PipeFlowSolver.TrySolveQFromDeltaP(
                0d,
                length_m: 1d,
                diameter_m: 0.02d,
                roughness_m: 1e-5d,
                minorLossCoefficients: null,
                density_kgm3: 998d,
                dynamicViscosity_Pa_s: 1e-3d,
                relTol: 1e-6d,
                absTol: 1e-9d,
                maxSolveIterations: 64,
                qMaxHint: 0.01d,
                out double q);

            Assert.IsTrue(solved, "Zero ΔP should be solvable.");
            Assert.AreEqual(0d, q, 1e-12d, "Flow should be zero when ΔP is zero.");
        }

        [Test]
        public void LargerDeltaP_ProducesLargerFlow()
        {
            double qLow;
            bool solvedLow = PipeFlowSolver.TrySolveQFromDeltaP(
                100d,
                length_m: 2d,
                diameter_m: 0.015d,
                roughness_m: 1e-5d,
                minorLossCoefficients: new[] { 0.5 },
                density_kgm3: 998d,
                dynamicViscosity_Pa_s: 1e-3d,
                relTol: 1e-6d,
                absTol: 1e-9d,
                maxSolveIterations: 128,
                qMaxHint: 0.01d,
                out qLow);

            double qHigh;
            bool solvedHigh = PipeFlowSolver.TrySolveQFromDeltaP(
                300d,
                length_m: 2d,
                diameter_m: 0.015d,
                roughness_m: 1e-5d,
                minorLossCoefficients: new[] { 0.5 },
                density_kgm3: 998d,
                dynamicViscosity_Pa_s: 1e-3d,
                relTol: 1e-6d,
                absTol: 1e-9d,
                maxSolveIterations: 128,
                qMaxHint: 0.01d,
                out qHigh);

            Assert.IsTrue(solvedLow && solvedHigh, "Solutions should converge for valid inputs.");
            Assert.Greater(qHigh, qLow, "Higher ΔP should yield higher Q.");
        }

        [Test]
        public void ForwardAndInverseAgreeWithinTolerance()
        {
            const double targetQ = 0.005d;
            double deltaP = PipeFlowSolver.DeltaP_Model(
                targetQ,
                length_m: 3d,
                diameter_m: 0.02d,
                roughness_m: 1e-5d,
                density_kgm3: 998d,
                dynamicViscosity_Pa_s: 1e-3d,
                minorLossCoefficients: new[] { 1.5d });

            bool solved = PipeFlowSolver.TrySolveQFromDeltaP(
                deltaP,
                length_m: 3d,
                diameter_m: 0.02d,
                roughness_m: 1e-5d,
                minorLossCoefficients: new[] { 1.5d },
                density_kgm3: 998d,
                dynamicViscosity_Pa_s: 1e-3d,
                relTol: 1e-6d,
                absTol: 1e-9d,
                maxSolveIterations: 128,
                qMaxHint: 0.01d,
                out double solvedQ);

            Assert.IsTrue(solved, "Back-solve should converge when starting from a valid ΔP.");
            double tolerance = System.Math.Max(1e-8d, 1e-4d * targetQ);
            Assert.AreEqual(targetQ, solvedQ, tolerance, "Inverse solve should reconstruct the original flow.");
        }
    }
}
