using System;
using UnityEngine;

namespace MachineRepair.Fluid
{
    public enum HydraulicEdgeKind
    {
        Unknown = 0,
        Pipe = 1,
        InternalConnector = 2
    }

    public struct HydraulicNode
    {
        public int Id;
        public Vector2Int Cell;
        public bool IsFixedPressure;
        public bool IsSource;
        public double FixedPressure_Pa;
        public double SourcePressure_Pa;
        public double Injection_m3s;
        public double Pressure_Pa;

        public bool HasInjection => Math.Abs(Injection_m3s) > double.Epsilon;
    }

    public struct HydraulicEdge
    {
        public int Id;
        public int NodeA;
        public int NodeB;
        public IHydraulicElementModel Model;
        public HydraulicEdgeKind Kind;
        public double Flow_m3s;
        public double LastDeltaP_Pa;
        public object Tag;
    }

    public interface IHydraulicElementModel
    {
        double SolveFlow(double deltaP_Pa, HydraulicSolverSettings settings);
        double EvaluateDeltaP(double flow_m3s);
        double EstimateDerivative(double flow_m3s, double deltaQ);
        double LastDeltaP { get; }
    }

    public struct HydraulicSolverSettings
    {
        public double RelativeTolerance;
        public double AbsoluteTolerance;
        public int MaxEdgeSolveIterations;
        public int MaxNetworkIterations;
        public double ResidualTolerance;
        public double PressureRelaxation;
        public double MaxPressureStep;
        public double MinimumPressure;
        public double DerivativeFlowStep;

        public static HydraulicSolverSettings Default => new HydraulicSolverSettings
        {
            RelativeTolerance = 1e-6,
            AbsoluteTolerance = 1e-9,
            MaxEdgeSolveIterations = 64,
            MaxNetworkIterations = 32,
            ResidualTolerance = 1e-6,
            PressureRelaxation = 0.5,
            MaxPressureStep = 5_000d,
            MinimumPressure = 0d,
            DerivativeFlowStep = 1e-6
        };
    }

    public struct HydraulicSolveResult
    {
        public bool Converged;
        public int Iterations;
        public double MaxResidual;
    }
}
