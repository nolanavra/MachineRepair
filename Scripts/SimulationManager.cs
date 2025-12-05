using System;
using UnityEngine;
using MachineRepair.Grid;

namespace MachineRepair
{
    /// <summary>
    /// Deterministic, phase-based simulation driver. Runs the electrical, hydraulic,
    /// and signal passes in a predictable order so components and connectors remain
    /// stable between steps.
    /// </summary>
    public class SimulationManager : MonoBehaviour, IGameModeListener
    {
        [Header("References")]
        [SerializeField] private GridManager grid;

        [Header("Execution")]
        [Tooltip("Automatically run simulation steps while Simulation mode is active.")]
        [SerializeField] private bool autorun = true;
        [Tooltip("Seconds between autorun steps.")]
        [SerializeField] private float stepInterval = 0.1f;

        private float stepTimer;

        /// <summary>
        /// Raised after a simulation step finishes. UI can listen for snapshot updates.
        /// </summary>
        public event Action SimulationStepCompleted;

        private void Awake()
        {
            if (grid == null) grid = FindFirstObjectByType<GridManager>();
        }

        private void OnEnable()
        {
            if (GameModeManager.Instance != null)
            {
                GameModeManager.Instance.RegisterListener(this);
            }
        }

        private void OnDisable()
        {
            if (GameModeManager.Instance != null)
            {
                GameModeManager.Instance.UnregisterListener(this);
            }
        }

        private void Update()
        {
            if (!autorun) return;
            if (GameModeManager.Instance != null && GameModeManager.Instance.CurrentMode != GameMode.Simulation) return;
            if (grid == null) return;

            stepTimer += Time.deltaTime;
            if (stepTimer < stepInterval) return;

            stepTimer = 0f;
            RunSimulationStep();
        }

        /// <summary>
        /// Performs a single deterministic simulation step across all phases.
        /// Can be called manually from debug UI or tests to inspect state between steps.
        /// </summary>
        public void RunSimulationStep()
        {
            if (grid == null) return;

            BuildGraphs();
            PropagateVoltageCurrent();
            PropagatePressureFlow();
            EvaluateSignalStates();
            UpdateComponentBehaviors();
            DetectFaults();
            EmitSimulationSnapshot();

            SimulationStepCompleted?.Invoke();
        }

        public void OnEnterMode(GameMode newMode)
        {
            if (newMode != GameMode.Simulation)
            {
                stepTimer = 0f;
            }
        }

        public void OnExitMode(GameMode oldMode)
        {
            if (oldMode == GameMode.Simulation)
            {
                stepTimer = 0f;
            }
        }

        private void BuildGraphs()
        {
            // TODO: assemble electrical, hydraulic, and signal graphs from grid contents.
        }

        private void PropagateVoltageCurrent()
        {
            // TODO: propagate voltage and current along electrical graph using component behaviors.
        }

        private void PropagatePressureFlow()
        {
            // TODO: propagate pressure and flow along hydraulic graph using component behaviors.
        }

        private void EvaluateSignalStates()
        {
            // TODO: compute digital/logic signal states along signal graph.
        }

        private void UpdateComponentBehaviors()
        {
            // TODO: run per-component behavior updates based on propagated values.
        }

        private void DetectFaults()
        {
            // TODO: inspect components/connectors for fault conditions and log them.
        }

        private void EmitSimulationSnapshot()
        {
            // TODO: notify debug/inspector UI with the latest simulation values.
        }
    }
}
