using System;
using System.Collections.Generic;
using UnityEngine;

namespace MachineRepair.Fluid
{
    /// <summary>
    /// Usage:
    /// - Drop the component on any GameObject (recommended in an Editor-only scene object).
    /// - Enter pipe geometry, losses, and either a water preset or custom fluid properties.
    /// - Click "Solve Now" in the custom inspector to evaluate flow Q from a pressure drop.
    /// The math is delegated to <see cref="PipeFlowSolver"/> to keep simulation code pure.
    /// </summary>
    [ExecuteAlways]
    public class PipeFlowTool : MonoBehaviour
    {
        public enum WaterPreset
        {
            Custom = 0,
            Water20C = 1
        }

        [Header("Inputs (SI)")]
        [SerializeField] private double deltaP_Pa = 1000d;
        [SerializeField] private double length_m = 1d;
        [SerializeField] private double diameter_m = 0.01d;
        [SerializeField] private double roughness_m = 1e-5d;

        [Header("Fluid")]
        [SerializeField] private WaterPreset waterPreset = WaterPreset.Water20C;
        [SerializeField] private double rho_kgm3 = 998d;
        [SerializeField] private double mu_Pa_s = 1.002e-3d;

        [Header("Minor Losses (K)")]
        [SerializeField] private List<float> minorKs = new List<float>();
        [SerializeField, Tooltip("Optional extra ΣK added to the list values. Negative values are clamped to zero.")]
        private float kSum = 0f;

        [Header("Solver Tuning")]
        [SerializeField] private double qMaxHint = 0.01d;
        [SerializeField] private double relTol = 1e-6d;
        [SerializeField] private double absTol = 1e-9d;
        [SerializeField] private int maxIters = 64;
        [SerializeField] private bool solveOnValidate;

        [Header("Outputs (read-only)")]
        [SerializeField, Tooltip("Solved volumetric flow rate in m^3/s")] private double q_m3s;
        [SerializeField, Tooltip("Solved volumetric flow rate in L/min")] private double q_Lmin;
        [SerializeField] private double velocity_ms;
        [SerializeField] private double reynolds;
        [SerializeField] private double frictionFactor;
        [SerializeField] private double computedKSum;
        [SerializeField] private double predictedDeltaP;
        [SerializeField] private bool success;
        [SerializeField] private string lastError;

        private double[] minorBuffer = new double[0];

        public double Q_m3s => q_m3s;
        public double Q_Lmin => q_Lmin;
        public bool Success => success;
        public string LastError => lastError;

        private void OnValidate()
        {
            if (solveOnValidate)
            {
                Solve();
            }
        }

        public void Solve()
        {
            success = false;
            lastError = string.Empty;

            if (minorKs == null)
            {
                minorKs = new List<float>();
            }

            double resolvedRho = rho_kgm3;
            double resolvedMu = mu_Pa_s;
            if (waterPreset == WaterPreset.Water20C)
            {
                resolvedRho = 998d;
                resolvedMu = 1.002e-3d;
            }

            int kCount = minorKs.Count;
            bool includeKSum = kSum > 0f;
            int required = kCount + (includeKSum ? 1 : 0);
            EnsureBufferSize(required);
            ClearUnusedEntries(required);

            computedKSum = 0d;
            for (int i = 0; i < kCount; i++)
            {
                float k = minorKs[i];
                if (k > 0f)
                {
                    minorBuffer[i] = k;
                    computedKSum += k;
                }
                else
                {
                    minorBuffer[i] = 0d;
                }
            }

            if (includeKSum)
            {
                minorBuffer[kCount] = kSum;
                computedKSum += kSum;
            }

            double[] bufferForSolve = required > 0 ? minorBuffer : null;

            bool solved = PipeFlowSolver.TrySolveQFromDeltaP(
                deltaP_Pa,
                length_m,
                diameter_m,
                roughness_m,
                bufferForSolve,
                resolvedRho,
                resolvedMu,
                relTol,
                absTol,
                maxIters,
                qMaxHint,
                out q_m3s);

            success = solved;
            if (!solved)
            {
                q_m3s = 0d;
                q_Lmin = 0d;
                velocity_ms = 0d;
                reynolds = 0d;
                frictionFactor = 0d;
                predictedDeltaP = 0d;
                lastError = "Invalid inputs or unable to bracket the solution.";
                return;
            }

            double area = Math.PI * diameter_m * diameter_m * 0.25d;
            velocity_ms = area > 0d ? q_m3s / area : 0d;
            reynolds = diameter_m > 0d && resolvedMu > 0d
                ? resolvedRho * velocity_ms * diameter_m / resolvedMu
                : 0d;
            frictionFactor = PipeFlowSolver.FrictionFactor_Darcy(reynolds, roughness_m, diameter_m);

            q_Lmin = q_m3s * 1000d * 60d;
            predictedDeltaP = PipeFlowSolver.DeltaP_Model(
                q_m3s,
                length_m,
                diameter_m,
                roughness_m,
                resolvedRho,
                resolvedMu,
                bufferForSolve);

            lastError = string.Empty;
        }

        private void EnsureBufferSize(int required)
        {
            if (minorBuffer == null || minorBuffer.Length < required)
            {
                minorBuffer = required > 0 ? new double[required] : System.Array.Empty<double>();
            }
        }

        private void ClearUnusedEntries(int required)
        {
            if (minorBuffer == null)
            {
                return;
            }

            for (int i = required; i < minorBuffer.Length; i++)
            {
                minorBuffer[i] = 0d;
            }
        }
    }
}
