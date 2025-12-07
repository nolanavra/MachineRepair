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

        private float stepTimer;

        // Graph buffers (per cell) for electrical, hydraulic, and signal states.
        private float[] voltageGraph;
        private float[] currentGraph;
        private float[] pressureGraph;
        private float[] flowGraph;
        private bool[] signalGraph;

        private readonly List<LeakInfo> detectedLeaks = new();

        private readonly List<string> faultLog = new();

        public SimulationSnapshot? LastSnapshot { get; private set; }

        public bool PowerOn => powerOn;
        public bool WaterOn => waterOn;

        /// <summary>
        /// Raised after a simulation step finishes. UI can listen for snapshot updates.
        /// </summary>
        public event Action SimulationStepCompleted;
        public event Action<bool> PowerToggled;
        public event Action<bool> WaterToggled;
        public event Action<IReadOnlyList<LeakInfo>> LeaksUpdated;

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
            detectedLeaks.Clear();

            if (grid == null || pressureGraph == null || flowGraph == null)
            {
                NotifyLeakListeners();
                return;
            }

            if (!waterOn)
            {
                NotifyLeakListeners();
                return;
            }

            var waterPorts = new HashSet<Vector2Int>();
            CollectWaterPorts(waterPorts);

            var visited = new HashSet<int>();
            var queue = new Queue<Vector2Int>();

            for (int y = 0; y < grid.height; y++)
            {
                for (int x = 0; x < grid.width; x++)
                {
                    var cell = grid.GetCell(new Vector2Int(x, y));
                    if (!IsWaterSupplyComponent(cell)) continue;

                    if (!grid.InBounds(cell.component.anchorCell.x, cell.component.anchorCell.y)) continue;
                    int idx = grid.ToIndex(cell.component.anchorCell);

                    pressureGraph[idx] = cell.component.def.maxPressure;
                    flowGraph[idx] = Mathf.Max(0f, cell.component.def.flowCoef);

                    if (visited.Add(idx))
                    {
                        queue.Enqueue(cell.component.anchorCell);
                    }
                }
            }

            while (queue.Count > 0)
            {
                var cellPos = queue.Dequeue();
                int idx = grid.ToIndex(cellPos);

                float pressure = pressureGraph[idx];
                float flow = flowGraph[idx];
                if (pressure <= 0f && flow <= 0f) continue;

                var cell = grid.GetCell(cellPos);
                bool traversable = cell.HasPipe || IsWaterSupplyComponent(cell) || IsWaterPortCell(cellPos, waterPorts);
                if (!traversable) continue;

                for (int dir = 0; dir < 4; dir++)
                {
                    Vector2Int neighbor = dir switch
                    {
                        0 => new Vector2Int(cellPos.x + 1, cellPos.y),
                        1 => new Vector2Int(cellPos.x - 1, cellPos.y),
                        2 => new Vector2Int(cellPos.x, cellPos.y + 1),
                        _ => new Vector2Int(cellPos.x, cellPos.y - 1)
                    };

                    if (!grid.InBounds(neighbor.x, neighbor.y)) continue;

                    var neighborCell = grid.GetCell(neighbor);
                    bool acceptsWater = neighborCell.HasPipe || IsWaterPortCell(neighbor, waterPorts) || IsWaterSupplyComponent(neighborCell);
                    if (!acceptsWater) continue;

                    int nIdx = grid.ToIndex(neighbor);
                    if (pressure > pressureGraph[nIdx]) pressureGraph[nIdx] = pressure;
                    if (flow > flowGraph[nIdx]) flowGraph[nIdx] = flow;

                    if (visited.Add(nIdx))
                    {
                        queue.Enqueue(neighbor);
                    }
                }

                if (cell.HasPipe && !IsWaterPortCell(cellPos, waterPorts))
                {
                    int connections = CountWaterConnections(cellPos, waterPorts);
                    if (connections <= 1)
                    {
                        detectedLeaks.Add(new LeakInfo
                        {
                            Cell = cellPos,
                            WorldPosition = grid.CellToWorld(cellPos)
                        });
                    }
                }
            }

            NotifyLeakListeners();
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
                        foreach (var wire in cell.Wires)
                        {
                            if (wire == null) continue;
                            wire.voltage = voltageGraph[idx];
                            wire.current = currentGraph[idx];
                        }
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

                    if (cell.HasWire)
                    {
                        foreach (var wire in cell.Wires)
                        {
                            if (wire == null) continue;
                            if (wire.EvaluateDamage(maxCurrent: 20f, maxResistance: 10f))
                            {
                                faultLog.Add($"Wire damaged near {x},{y}");
                                break;
                            }
                        }
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

        private void CollectWaterPorts(HashSet<Vector2Int> waterPorts)
        {
            waterPorts.Clear();

            for (int y = 0; y < grid.height; y++)
            {
                for (int x = 0; x < grid.width; x++)
                {
                    var cell = grid.GetCell(new Vector2Int(x, y));
                    if (cell.component == null || cell.component.portDef == null || cell.component.portDef.ports == null) continue;

                    foreach (var port in cell.component.portDef.ports)
                    {
                        if (port.port != PortType.Water) continue;

                        Vector2Int global = cell.component.GetGlobalCell(port);
                        if (grid.InBounds(global.x, global.y))
                        {
                            waterPorts.Add(global);
                        }
                    }
                }
            }
        }

        private int CountWaterConnections(Vector2Int cellPos, HashSet<Vector2Int> waterPorts)
        {
            int connections = 0;

            for (int dir = 0; dir < 4; dir++)
            {
                Vector2Int neighbor = dir switch
                {
                    0 => new Vector2Int(cellPos.x + 1, cellPos.y),
                    1 => new Vector2Int(cellPos.x - 1, cellPos.y),
                    2 => new Vector2Int(cellPos.x, cellPos.y + 1),
                    _ => new Vector2Int(cellPos.x, cellPos.y - 1)
                };

                if (!grid.InBounds(neighbor.x, neighbor.y)) continue;

                var neighborCell = grid.GetCell(neighbor);
                if (neighborCell.HasPipe || IsWaterSupplyComponent(neighborCell) || IsWaterPortCell(neighbor, waterPorts))
                {
                    connections++;
                }
            }

            return connections;
        }

        private bool IsWaterSupplyComponent(cellDef cell)
        {
            return cell.component != null
                && cell.component.def != null
                && cell.component.def.type == ComponentType.ChassisWaterConnection;
        }

        private static bool IsWaterPortCell(Vector2Int cellPos, HashSet<Vector2Int> waterPorts)
        {
            return waterPorts.Contains(cellPos);
        }

        private void NotifyLeakListeners()
        {
            LeaksUpdated?.Invoke(detectedLeaks);
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

        public struct LeakInfo
        {
            public Vector2Int Cell;
            public Vector3 WorldPosition;
        }
    }
}
