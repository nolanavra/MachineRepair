using System;
using System.Collections.Generic;
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
        [Tooltip("Toggle electrical propagation on/off.")]
        [SerializeField] private bool powerOn = true;
        [Tooltip("Toggle hydraulic propagation on/off.")]
        [SerializeField] private bool waterOn = true;

        public bool WaterOn => waterOn;

        private float stepTimer;

        // Graph buffers (per cell) for electrical, hydraulic, and signal states.
        private float[] voltageGraph;
        private float[] currentGraph;
        private float[] pressureGraph;
        private float[] flowGraph;
        private bool[] signalGraph;

        private readonly List<string> faultLog = new();

        public SimulationSnapshot? LastSnapshot { get; private set; }

        /// <summary>
        /// Raised after a simulation step finishes. UI can listen for snapshot updates.
        /// </summary>
        public event Action SimulationStepCompleted;
        public event Action<bool> PowerToggled;
        public event Action<bool> WaterToggled;

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

            bool anyEnabled = powerOn || waterOn;
            if (!anyEnabled)
            {
                stepTimer = 0f;
                return;
            }

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

        public void SetPower(bool enabled)
        {
            if (powerOn == enabled) return;

            powerOn = enabled;
            stepTimer = 0f;
            PowerToggled?.Invoke(powerOn);
        }

        public void SetWater(bool enabled)
        {
            if (waterOn == enabled) return;

            waterOn = enabled;
            stepTimer = 0f;
            WaterToggled?.Invoke(waterOn);
        }

        private void BuildGraphs()
        {
            if (grid == null) return;

            int cellCount = grid.CellCount;
            EnsureGraph(ref voltageGraph, cellCount);
            EnsureGraph(ref currentGraph, cellCount);
            EnsureGraph(ref pressureGraph, cellCount);
            EnsureGraph(ref flowGraph, cellCount);
            EnsureGraph(ref signalGraph, cellCount);

            faultLog.Clear();
        }

        private void PropagateVoltageCurrent()
        {
            if (grid == null || voltageGraph == null || currentGraph == null) return;

            for (int y = 0; y < grid.height; y++)
            {
                for (int x = 0; x < grid.width; x++)
                {
                    var cell = grid.GetCell(new Vector2Int(x, y));
                    if (cell.component == null || cell.component.def == null) continue;

                    if (cell.component.def.type != ComponentType.ChassisPowerConnection) continue;

                    if (!grid.InBounds(cell.component.anchorCell.x, cell.component.anchorCell.y)) continue;
                    int idx = grid.ToIndex(cell.component.anchorCell);

                    float supplyVoltage = Mathf.Max(cell.component.def.maxACVoltage, cell.component.def.maxDCVoltage);
                    float supplyCurrent = supplyVoltage > 0f && cell.component.def.wattage > 0f
                        ? cell.component.def.wattage / supplyVoltage
                        : 0f;

                    voltageGraph[idx] = supplyVoltage;
                    currentGraph[idx] = supplyCurrent;
                }
            }
        }

        private void PropagatePressureFlow()
        {
            if (grid == null || pressureGraph == null || flowGraph == null) return;

            for (int y = 0; y < grid.height; y++)
            {
                for (int x = 0; x < grid.width; x++)
                {
                    var cell = grid.GetCell(new Vector2Int(x, y));
                    if (cell.component == null || cell.component.def == null) continue;

                    if (cell.component.def.type != ComponentType.ChassisWaterConnection) continue;

                    if (!grid.InBounds(cell.component.anchorCell.x, cell.component.anchorCell.y)) continue;
                    int idx = grid.ToIndex(cell.component.anchorCell);

                    pressureGraph[idx] = cell.component.def.maxPressure;
                    flowGraph[idx] = Mathf.Max(0f, cell.component.def.flowCoef);
                }
            }
        }

        private void EvaluateSignalStates()
        {
            if (signalGraph == null || voltageGraph == null) return;

            for (int i = 0; i < signalGraph.Length; i++)
            {
                signalGraph[i] = voltageGraph[i] > 0.01f;
            }
        }

        private void UpdateComponentBehaviors()
        {
            if (grid == null || voltageGraph == null) return;

            for (int y = 0; y < grid.height; y++)
            {
                for (int x = 0; x < grid.width; x++)
                {
                    var cell = grid.GetCell(new Vector2Int(x, y));
                    if (cell.component == null || cell.component.def == null) continue;

                    if (!grid.InBounds(cell.component.anchorCell.x, cell.component.anchorCell.y)) continue;
                    int idx = grid.ToIndex(cell.component.anchorCell);

                    // For now, mirror propagated electrical values into any wires located in the same cell.
                    if (cell.HasWire)
                    {
                        cell.wire.voltage = voltageGraph[idx];
                        cell.wire.current = currentGraph[idx];
                    }
                }
            }
        }

        private void DetectFaults()
        {
            if (grid == null || voltageGraph == null) return;

            faultLog.Clear();

            for (int y = 0; y < grid.height; y++)
            {
                for (int x = 0; x < grid.width; x++)
                {
                    var cell = grid.GetCell(new Vector2Int(x, y));
                    if (cell.component != null && cell.component.def != null)
                    {
                        int idx = grid.ToIndex(cell.component.anchorCell);
                        if (!grid.InBounds(cell.component.anchorCell.x, cell.component.anchorCell.y)) continue;

                        if (cell.component.def.requiresPower && voltageGraph[idx] <= 0f)
                        {
                            faultLog.Add($"{cell.component.def.displayName} lacks power at {cell.component.anchorCell}");
                        }
                    }

                    if (cell.HasWire && cell.wire.EvaluateDamage(maxCurrent: 20f, maxResistance: 10f))
                    {
                        faultLog.Add($"Wire damaged near {x},{y}");
                    }
                }
            }
        }

        private void EmitSimulationSnapshot()
        {
            if (voltageGraph == null || currentGraph == null || pressureGraph == null || flowGraph == null || signalGraph == null)
            {
                return;
            }

            LastSnapshot = new SimulationSnapshot
            {
                Voltage = (float[])voltageGraph.Clone(),
                Current = (float[])currentGraph.Clone(),
                Pressure = (float[])pressureGraph.Clone(),
                Flow = (float[])flowGraph.Clone(),
                Signals = (bool[])signalGraph.Clone(),
                Faults = new List<string>(faultLog)
            };

            SimulationStepCompleted?.Invoke();
        }

        private static void EnsureGraph(ref float[] graph, int size)
        {
            if (graph == null || graph.Length != size)
            {
                graph = new float[size];
            }
            else
            {
                System.Array.Clear(graph, 0, graph.Length);
            }
        }

        private static void EnsureGraph(ref bool[] graph, int size)
        {
            if (graph == null || graph.Length != size)
            {
                graph = new bool[size];
            }
            else
            {
                System.Array.Clear(graph, 0, graph.Length);
            }
        }

        public struct SimulationSnapshot
        {
            public float[] Voltage;
            public float[] Current;
            public float[] Pressure;
            public float[] Flow;
            public bool[] Signals;
            public IReadOnlyList<string> Faults;
        }
    }
}
