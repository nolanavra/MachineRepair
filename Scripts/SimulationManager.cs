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
    public class SimulationManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GridManager grid;

        [Header("Execution")]
        [Tooltip("Automatically run simulation steps while simulation is running.")]
        [SerializeField] private bool autorun = true;
        [Tooltip("Seconds between autorun steps.")]
        [SerializeField] private float stepInterval = 0.1f;
        [Tooltip("Toggle electrical propagation on/off.")]
        [SerializeField] private bool powerOn = true;
        [Tooltip("Toggle hydraulic propagation on/off.")]
        [SerializeField] private bool waterOn = true;
        [Tooltip("Whether the simulation loop is currently advancing.")]
        [SerializeField] private bool simulationRunning;
        [Header("Debugging")]
        [Tooltip("Log water flow arrow instantiation for debugging hydraulic paths.")]
        [SerializeField] private bool logWaterFlowPaths = false;

        private float stepTimer;
        private int simulationStepCount;

        // Graph buffers (per cell) for electrical, hydraulic, and signal states.
        private float[] pressureGraph;
        private float[] flowGraph;
        private bool[] signalGraph;
        private int[] waterArrowSteps;

        private readonly List<LeakInfo> detectedLeaks = new();

        private readonly List<string> faultLog = new();
        private readonly HashSet<MachineComponent> componentsMissingReturn = new();
        private readonly HashSet<PlacedWire> completedCircuitWires = new();
        private readonly List<WaterFlowArrow> waterFlowArrows = new();

        private readonly HashSet<MachineComponent> poweredComponents = new();
        private readonly HashSet<PlacedWire> poweredWires = new();
        private readonly Dictionary<MachineComponent, float> componentVoltage = new();
        private readonly Dictionary<MachineComponent, float> componentCurrent = new();
        private readonly Dictionary<PlacedWire, float> wireVoltage = new();
        private readonly Dictionary<PlacedWire, float> wireCurrent = new();
        private readonly List<List<Vector2Int>> poweredLoops = new();

        private static readonly Vector2Int[] WaterPropagationOffsets =
        {
            new Vector2Int(1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int(0, 1),
            new Vector2Int(0, -1),
            new Vector2Int(1, 1),
            new Vector2Int(-1, 1),
            new Vector2Int(1, -1),
            new Vector2Int(-1, -1)
        };

        public SimulationSnapshot? LastSnapshot { get; private set; }

        public bool PowerOn => powerOn;
        public bool WaterOn => waterOn;
        public bool SimulationRunning => simulationRunning;
        public int SimulationStepCount => simulationStepCount;
        public IReadOnlyCollection<MachineComponent> ComponentsMissingReturn => componentsMissingReturn;

        /// <summary>
        /// Raised after a simulation step finishes. UI can listen for snapshot updates.
        /// </summary>
        public event Action SimulationStepCompleted;
        public event Action<bool> PowerToggled;
        public event Action<bool> WaterToggled;
        public event Action<IReadOnlyList<LeakInfo>> LeaksUpdated;
        public event Action<IReadOnlyList<WaterFlowArrow>> WaterFlowUpdated;
        public event Action<bool> SimulationRunStateChanged;

        public struct WaterFlowArrow
        {
            public Vector3 Position;
            public Vector2 Direction;
            public float Speed;
        }

        private void Awake()
        {
            if (grid == null) grid = FindFirstObjectByType<GridManager>();
        }

        private void Update()
        {
            if (!autorun) return;
            if (!simulationRunning)
            {
                stepTimer = 0f;
                return;
            }
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

        public void SetSimulationRunning(bool shouldRun)
        {
            if (simulationRunning == shouldRun) return;

            simulationRunning = shouldRun;
            stepTimer = 0f;
            SimulationRunStateChanged?.Invoke(simulationRunning);
        }

        public void StartSimulation() => SetSimulationRunning(true);

        public void StopSimulation() => SetSimulationRunning(false);

        public void ToggleSimulationRunning() => SetSimulationRunning(!simulationRunning);

        public void SetPower(bool enabled)
        {
            if (powerOn == enabled) return;

            powerOn = enabled;
            stepTimer = 0f;
            UpdateChassisPowerAvailability(powerOn);
            ApplyWireBloom(completedCircuitWires, powerOn);
            PowerToggled?.Invoke(powerOn);

            if (!powerOn)
            {
                DepowerAllComponents();
                DepowerAllWires();
            }
        }

        public void SetWater(bool enabled)
        {
            if (waterOn == enabled) return;

            waterOn = enabled;
            stepTimer = 0f;
            WaterToggled?.Invoke(waterOn);

            ClearWaterFlowArrows();
        }

        private void BuildGraphs()
        {
            if (grid == null) return;

            int cellCount = grid.CellCount;
            EnsureGraph(ref pressureGraph, cellCount);
            EnsureGraph(ref flowGraph, cellCount);
            EnsureGraph(ref signalGraph, cellCount);
            EnsureGraph(ref waterArrowSteps, cellCount, -1);

            faultLog.Clear();
            componentsMissingReturn.Clear();
        }

        private void PropagateVoltageCurrent()
        {
            if (grid == null) return;

            poweredLoops.Clear();
            if (!powerOn)
            {
                DepowerAllComponents();
                DepowerAllWires();
                return;
            }

            var portByCell = new Dictionary<Vector2Int, PowerPort>();
            var chassisPorts = new List<PowerPort>();

            CollectPowerPorts(portByCell, chassisPorts);

            var wires = CollectPowerWires();
            var powerGraph = BuildPowerGraph(wires, portByCell);
            var loops = BuildChassisLoops(chassisPorts, powerGraph.Adjacency);

            poweredComponents.Clear();
            poweredWires.Clear();
            componentVoltage.Clear();
            componentCurrent.Clear();
            wireVoltage.Clear();
            wireCurrent.Clear();

            ApplyPoweredLoops(loops, portByCell, powerGraph.WireByEdge);

            UpdatePoweredCircuitWires(poweredWires);
            DepowerUnvisitedComponents();
        }

        private void CollectPowerPorts(
            Dictionary<Vector2Int, PowerPort> portByCell,
            List<PowerPort> chassisPorts)
        {
            portByCell.Clear();
            chassisPorts.Clear();

            for (int y = 0; y < grid.height; y++)
            {
                for (int x = 0; x < grid.width; x++)
                {
                    var cell = grid.GetCell(new Vector2Int(x, y));
                    if (cell.component == null || cell.component.def == null) continue;

                    if (cell.component.portDef == null || cell.component.portDef.ports == null) continue;

                    for (int i = 0; i < cell.component.portDef.ports.Length; i++)
                    {
                        var port = cell.component.portDef.ports[i];
                        if (port.port != PortType.Power) continue;

                        Vector2Int global = cell.component.GetGlobalCell(port);
                        if (!grid.InBounds(global.x, global.y)) continue;

                        float voltage = Mathf.Max(cell.component.def.maxACVoltage, cell.component.def.maxDCVoltage);
                        float current = voltage > 0f && cell.component.def.wattage > 0f
                            ? cell.component.def.wattage / voltage
                            : 0f;

                        var powerPort = new PowerPort
                        {
                            Cell = global,
                            Component = cell.component,
                            IsInput = port.isInput,
                            Voltage = voltage,
                            Current = current
                        };

                        portByCell[global] = powerPort;

                        if (cell.component.def.type == ComponentType.ChassisPowerConnection)
                        {
                            chassisPorts.Add(powerPort);
                        }
                    }
                }
            }
        }

        private List<PlacedWire> CollectPowerWires()
        {
            var wires = new List<PlacedWire>();
            var seen = new HashSet<PlacedWire>();

            for (int y = 0; y < grid.height; y++)
            {
                for (int x = 0; x < grid.width; x++)
                {
                    var cell = grid.GetCell(new Vector2Int(x, y));
                    if (!cell.HasWire) continue;

                    foreach (var wire in cell.Wires)
                    {
                        if (wire == null || wire.wireType == WireType.Signal) continue;
                        if (seen.Add(wire))
                        {
                            wires.Add(wire);
                        }
                    }
                }
            }

            return wires;
        }

        private PowerGraph BuildPowerGraph(
            IEnumerable<PlacedWire> wires,
            Dictionary<Vector2Int, PowerPort> portByCell)
        {
            var graph = new PowerGraph
            {
                Adjacency = new Dictionary<Vector2Int, List<Vector2Int>>(),
                WireByEdge = new Dictionary<(Vector2Int, Vector2Int), PlacedWire>()
            };

            foreach (var wire in wires)
            {
                if (wire == null) continue;

                AddWireEdge(wire.startPortCell, wire.endPortCell, wire);
            }

            var portsByComponent = new Dictionary<MachineComponent, List<PowerPort>>();
            foreach (var kvp in portByCell)
            {
                if (kvp.Value.Component == null) continue;

                if (!portsByComponent.TryGetValue(kvp.Value.Component, out var list))
                {
                    list = new List<PowerPort>();
                    portsByComponent[kvp.Value.Component] = list;
                }

                list.Add(kvp.Value);
            }

            foreach (var kvp in portsByComponent)
            {
                var inputs = new List<PowerPort>();
                var outputs = new List<PowerPort>();

                foreach (var port in kvp.Value)
                {
                    if (port.IsInput)
                    {
                        inputs.Add(port);
                    }
                    else
                    {
                        outputs.Add(port);
                    }
                }

                foreach (var input in inputs)
                {
                    foreach (var output in outputs)
                    {
                        AddDirectionalEdge(input.Cell, output.Cell);
                        AddDirectionalEdge(output.Cell, input.Cell);
                    }
                }
            }

            foreach (var port in portByCell.Keys)
            {
                if (!graph.Adjacency.ContainsKey(port))
                {
                    graph.Adjacency[port] = new List<Vector2Int>();
                }
            }

            return graph;

            void AddWireEdge(Vector2Int a, Vector2Int b, PlacedWire wire)
            {
                AddDirectionalEdge(a, b);
                AddDirectionalEdge(b, a);
                graph.WireByEdge[(a, b)] = wire;
                graph.WireByEdge[(b, a)] = wire;
            }

            void AddDirectionalEdge(Vector2Int a, Vector2Int b)
            {
                if (!grid.InBounds(a.x, a.y) || !grid.InBounds(b.x, b.y)) return;

                if (!graph.Adjacency.TryGetValue(a, out var list))
                {
                    list = new List<Vector2Int>();
                    graph.Adjacency[a] = list;
                }

                if (!list.Contains(b))
                {
                    list.Add(b);
                }
            }
        }

        private List<List<Vector2Int>> BuildChassisLoops(
            IReadOnlyList<PowerPort> chassisPorts,
            Dictionary<Vector2Int, List<Vector2Int>> adjacency)
        {
            var loops = new List<List<Vector2Int>>();

            for (int i = 0; i < chassisPorts.Count; i++)
            {
                for (int j = i + 1; j < chassisPorts.Count; j++)
                {
                    EnumerateSimplePaths(chassisPorts[i].Cell, chassisPorts[j].Cell, adjacency, loops);
                }
            }

            return loops;
        }

        private void EnumerateSimplePaths(
            Vector2Int start,
            Vector2Int end,
            Dictionary<Vector2Int, List<Vector2Int>> adjacency,
            List<List<Vector2Int>> results)
        {
            if (!grid.InBounds(start.x, start.y) || !grid.InBounds(end.x, end.y)) return;

            var path = new List<Vector2Int>();
            var visited = new HashSet<Vector2Int>();

            void Dfs(Vector2Int node)
            {
                path.Add(node);
                visited.Add(node);

                if (node == end)
                {
                    results.Add(new List<Vector2Int>(path));
                }
                else if (adjacency.TryGetValue(node, out var neighbors))
                {
                    for (int i = 0; i < neighbors.Count; i++)
                    {
                        var next = neighbors[i];
                        if (visited.Contains(next)) continue;
                        Dfs(next);
                    }
                }

                visited.Remove(node);
                path.RemoveAt(path.Count - 1);
            }

            Dfs(start);
        }

        private void ApplyPoweredLoops(
            IEnumerable<List<Vector2Int>> loops,
            Dictionary<Vector2Int, PowerPort> portByCell,
            Dictionary<(Vector2Int, Vector2Int), PlacedWire> wireByEdge)
        {
            foreach (var loop in loops)
            {
                if (loop == null || loop.Count < 2) continue;

                float loopVoltage = 0f;
                float loopCurrent = 0f;
                var componentPorts = new Dictionary<MachineComponent, ComponentPortState>();

                foreach (var node in loop)
                {
                    if (portByCell.TryGetValue(node, out var port))
                    {
                        loopVoltage = Mathf.Max(loopVoltage, port.Voltage);
                        loopCurrent = Mathf.Max(loopCurrent, port.Current);

                        if (port.Component != null)
                        {
                            if (!componentPorts.TryGetValue(port.Component, out var state))
                            {
                                state = new ComponentPortState();
                            }

                            state.Register(port);
                            componentPorts[port.Component] = state;
                        }
                    }
                }

                for (int i = 0; i < loop.Count - 1; i++)
                {
                    var a = loop[i];
                    var b = loop[i + 1];

                    if (wireByEdge.TryGetValue((a, b), out var wire) || wireByEdge.TryGetValue((b, a), out wire))
                    {
                        ApplyWirePower(wire, loopVoltage, loopCurrent);
                        poweredWires.Add(wire);
                    }
                }

                foreach (var kvp in componentPorts)
                {
                    var component = kvp.Key;
                    var state = kvp.Value;

                    if (component == null || component.def == null) continue;

                    if (!state.HasInbound || !state.HasOutbound)
                    {
                        if (component.def.requiresPower)
                        {
                            componentsMissingReturn.Add(component);
                        }

                        continue;
                    }

                    ApplyComponentPower(component, loopVoltage, loopCurrent);
                }

                poweredLoops.Add(new List<Vector2Int>(loop));
            }
        }

        private struct PowerGraph
        {
            public Dictionary<Vector2Int, List<Vector2Int>> Adjacency;
            public Dictionary<(Vector2Int, Vector2Int), PlacedWire> WireByEdge;
        }

        private struct ComponentPortState
        {
            public bool HasInbound;
            public bool HasOutbound;

            public void Register(PowerPort port)
            {
                if (port.IsInput)
                {
                    HasInbound = true;
                }
                else
                {
                    HasOutbound = true;
                }
            }
        }

        private void ApplyComponentPower(MachineComponent component, float voltage, float current)
        {
            if (component == null) return;

            component.SetPowered(true);
            poweredComponents.Add(component);

            if (componentVoltage.TryGetValue(component, out var existingVoltage))
            {
                voltage = Mathf.Max(existingVoltage, voltage);
            }

            if (componentCurrent.TryGetValue(component, out var existingCurrent))
            {
                current = Mathf.Max(existingCurrent, current);
            }

            componentVoltage[component] = voltage;
            componentCurrent[component] = current;
        }

        private void ApplyWirePower(PlacedWire wire, float voltage, float current)
        {
            if (wire == null) return;

            wire.voltage = Mathf.Max(wire.voltage, voltage);
            wire.current = Mathf.Max(wire.current, current);
            wire.SetCircuitPowered(true);

            if (wireVoltage.TryGetValue(wire, out var existingVoltage))
            {
                voltage = Mathf.Max(existingVoltage, voltage);
            }

            if (wireCurrent.TryGetValue(wire, out var existingCurrent))
            {
                current = Mathf.Max(existingCurrent, current);
            }

            wireVoltage[wire] = voltage;
            wireCurrent[wire] = current;
        }

        private void DepowerAllComponents()
        {
            if (grid == null) return;

            poweredComponents.Clear();
            componentVoltage.Clear();
            componentCurrent.Clear();

            foreach (var component in EnumerateComponents())
            {
                component?.SetPowered(false);
            }
        }

        private void DepowerUnvisitedComponents()
        {
            foreach (var component in EnumerateComponents())
            {
                if (component == null) continue;
                if (poweredComponents.Contains(component)) continue;

                component.SetPowered(false);
            }
        }

        private void DepowerAllWires()
        {
            if (grid == null) return;

            poweredWires.Clear();
            wireVoltage.Clear();
            wireCurrent.Clear();
            completedCircuitWires.Clear();
            poweredLoops.Clear();

            var wires = CollectPowerWires();
            foreach (var wire in wires)
            {
                if (wire == null) continue;
                wire.voltage = 0f;
                wire.current = 0f;
                wire.SetCircuitPowered(false);
            }
        }

        private IEnumerable<MachineComponent> EnumerateComponents()
        {
            if (grid == null) yield break;

            var seen = new HashSet<MachineComponent>();

            for (int y = 0; y < grid.height; y++)
            {
                for (int x = 0; x < grid.width; x++)
                {
                    var cell = grid.GetCell(new Vector2Int(x, y));
                    if (cell.component == null) continue;

                    if (seen.Add(cell.component))
                    {
                        yield return cell.component;
                    }
                }
            }
        }

        private void UpdatePoweredCircuitWires(IEnumerable<PlacedWire> poweredWires)
        {
            completedCircuitWires.Clear();

            foreach (var entry in poweredWires)
            {
                if (entry == null) continue;
                completedCircuitWires.Add(entry);
            }

            ApplyWireBloom(completedCircuitWires, powerOn);
        }

        private void ApplyWireBloom(IEnumerable<PlacedWire> closedCircuitWires, bool powerEnabled)
        {
            if (grid == null) return;

            var closedSet = closedCircuitWires is HashSet<PlacedWire> hash
                ? hash
                : new HashSet<PlacedWire>(closedCircuitWires ?? Array.Empty<PlacedWire>());

            var allWires = CollectPowerWires();
            foreach (var wire in allWires)
            {
                if (wire == null) continue;
                bool energized = powerEnabled && closedSet.Contains(wire);
                wire.SetCircuitPowered(energized);
            }
        }

        private struct PowerPort
        {
            public Vector2Int Cell;
            public MachineComponent Component;
            public bool IsInput;
            public float Voltage;
            public float Current;
        }

        private void PropagatePressureFlow()
        {
            detectedLeaks.Clear();
            waterFlowArrows.Clear();

            if (grid == null || pressureGraph == null || flowGraph == null)
            {
                NotifyLeakListeners();
                NotifyWaterFlowListeners();
                return;
            }

            if (!waterOn)
            {
                NotifyLeakListeners();
                NotifyWaterFlowListeners();
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
                    waterArrowSteps[idx] = 0;

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
                int stepCount = Mathf.Max(0, waterArrowSteps[idx]);

                float pressure = pressureGraph[idx];
                float flow = flowGraph[idx];
                if (pressure <= 0f && flow <= 0f) continue;

                var cell = grid.GetCell(cellPos);
                bool traversable = cell.HasPipe || IsWaterSupplyComponent(cell) || IsWaterPortCell(cellPos, waterPorts);
                if (!traversable) continue;

                foreach (var offset in WaterPropagationOffsets)
                {
                    Vector2Int neighbor = cellPos + offset;

                    if (!grid.InBounds(neighbor.x, neighbor.y)) continue;

                    var neighborCell = grid.GetCell(neighbor);
                    bool acceptsWater = neighborCell.HasPipe || IsWaterPortCell(neighbor, waterPorts) || IsWaterSupplyComponent(neighborCell);
                    if (!acceptsWater) continue;

                    int nIdx = grid.ToIndex(neighbor);
                    bool propagated = false;
                    if (pressure > pressureGraph[nIdx])
                    {
                        pressureGraph[nIdx] = pressure;
                        propagated = true;
                    }
                    if (flow > flowGraph[nIdx])
                    {
                        flowGraph[nIdx] = flow;
                        propagated = true;
                    }

                    int nextStep = stepCount + 1;
                    if (waterArrowSteps[nIdx] == -1 || nextStep < waterArrowSteps[nIdx])
                    {
                        waterArrowSteps[nIdx] = nextStep;
                    }

                    if (flow > 0f && propagated && ShouldSpawnWaterArrow(nextStep))
                    {
                        AddWaterFlowArrow(cellPos, neighbor, flow);
                    }

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
            NotifyWaterFlowListeners();
        }

        private void EvaluateSignalStates()
        {
            if (signalGraph == null || grid == null) return;

            System.Array.Clear(signalGraph, 0, signalGraph.Length);

            foreach (var component in poweredComponents)
            {
                if (component == null) continue;
                Vector2Int anchor = component.anchorCell;
                if (!grid.InBounds(anchor.x, anchor.y)) continue;

                int idx = grid.ToIndex(anchor);
                if (idx >= 0 && idx < signalGraph.Length)
                {
                    signalGraph[idx] = true;
                }
            }

            foreach (var wire in poweredWires)
            {
                if (wire == null) continue;

                foreach (var cell in wire.occupiedCells)
                {
                    if (!grid.InBounds(cell.x, cell.y)) continue;
                    int idx = grid.ToIndex(cell);
                    if (idx >= 0 && idx < signalGraph.Length)
                    {
                        signalGraph[idx] = true;
                    }
                }
            }
        }

        private void UpdateComponentBehaviors()
        {
            // Intentionally left minimal until component-specific behavior is defined.
        }

        private void DetectFaults()
        {
            if (grid == null) return;

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

                        if (cell.component.def.requiresPower && componentsMissingReturn.Contains(cell.component))
                        {
                            faultLog.Add($"{cell.component.def.displayName} missing return path at {cell.component.anchorCell}");
                        }
                        else if (cell.component.def.requiresPower && !cell.component.IsPowered)
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
            if (grid == null || pressureGraph == null || flowGraph == null || signalGraph == null)
            {
                return;
            }

            simulationStepCount++;

            var voltageSnapshot = BuildVoltageSnapshot();
            var currentSnapshot = BuildCurrentSnapshot();

            LastSnapshot = new SimulationSnapshot
            {
                StepIndex = simulationStepCount,
                Voltage = voltageSnapshot,
                Current = currentSnapshot,
                Pressure = (float[])pressureGraph.Clone(),
                Flow = (float[])flowGraph.Clone(),
                Signals = (bool[])signalGraph.Clone(),
                Faults = new List<string>(faultLog),
                PowerLoops = CloneLoops(poweredLoops)
            };

            SimulationStepCompleted?.Invoke();
        }

        private float[] BuildVoltageSnapshot()
        {
            var voltage = new float[grid.CellCount];

            foreach (var kvp in componentVoltage)
            {
                var component = kvp.Key;
                if (component == null) continue;
                Vector2Int anchor = component.anchorCell;
                if (!grid.InBounds(anchor.x, anchor.y)) continue;

                int idx = grid.ToIndex(anchor);
                voltage[idx] = Mathf.Max(voltage[idx], kvp.Value);
            }

            foreach (var kvp in wireVoltage)
            {
                var wire = kvp.Key;
                if (wire == null) continue;

                foreach (var cell in wire.occupiedCells)
                {
                    if (!grid.InBounds(cell.x, cell.y)) continue;
                    int idx = grid.ToIndex(cell);
                    voltage[idx] = Mathf.Max(voltage[idx], kvp.Value);
                }
            }

            return voltage;
        }

        private float[] BuildCurrentSnapshot()
        {
            var current = new float[grid.CellCount];

            foreach (var kvp in componentCurrent)
            {
                var component = kvp.Key;
                if (component == null) continue;
                Vector2Int anchor = component.anchorCell;
                if (!grid.InBounds(anchor.x, anchor.y)) continue;

                int idx = grid.ToIndex(anchor);
                current[idx] = Mathf.Max(current[idx], kvp.Value);
            }

            foreach (var kvp in wireCurrent)
            {
                var wire = kvp.Key;
                if (wire == null) continue;

                foreach (var cell in wire.occupiedCells)
                {
                    if (!grid.InBounds(cell.x, cell.y)) continue;
                    int idx = grid.ToIndex(cell);
                    current[idx] = Mathf.Max(current[idx], kvp.Value);
                }
            }

            return current;
        }

        private static List<List<Vector2Int>> CloneLoops(IReadOnlyList<List<Vector2Int>> loops)
        {
            var copy = new List<List<Vector2Int>>(loops?.Count ?? 0);

            if (loops == null)
            {
                return copy;
            }

            foreach (var loop in loops)
            {
                if (loop == null) continue;
                copy.Add(new List<Vector2Int>(loop));
            }

            return copy;
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

            foreach (var offset in WaterPropagationOffsets)
            {
                Vector2Int neighbor = cellPos + offset;

                if (!grid.InBounds(neighbor.x, neighbor.y)) continue;

                var neighborCell = grid.GetCell(neighbor);
                if (neighborCell.HasPipe || IsWaterSupplyComponent(neighborCell) || IsWaterPortCell(neighbor, waterPorts))
                {
                    connections++;
                }
            }

            return connections;
        }

        private void AddWaterFlowArrow(Vector2Int from, Vector2Int to, float flow)
        {
            if (grid == null) return;

            Vector2 direction = to - from;
            if (direction == Vector2.zero) return;

            var arrow = new WaterFlowArrow
            {
                Position = grid.CellToWorld(from),
                Direction = direction.normalized,
                Speed = Mathf.Max(0f, flow)
            };

            waterFlowArrows.Add(arrow);

            if (logWaterFlowPaths)
            {
                Debug.Log($"[SimulationManager] Added water arrow from {from} toward {to} dir={arrow.Direction} speed={arrow.Speed:0.###}");
            }
        }

        private static bool ShouldSpawnWaterArrow(int stepsFromSource)
        {
            return stepsFromSource > 0 && stepsFromSource % 4 == 0;
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

        private void NotifyWaterFlowListeners()
        {
            WaterFlowUpdated?.Invoke(waterFlowArrows);
        }

        private void ClearWaterFlowArrows()
        {
            waterFlowArrows.Clear();
            NotifyWaterFlowListeners();
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

        private static void EnsureGraph(ref int[] graph, int size, int clearValue)
        {
            if (graph == null || graph.Length != size)
            {
                graph = new int[size];
            }

            System.Array.Fill(graph, clearValue);
        }

        public struct SimulationSnapshot
        {
            public int StepIndex;
            public float[] Voltage;
            public float[] Current;
            public float[] Pressure;
            public float[] Flow;
            public bool[] Signals;
            public IReadOnlyList<string> Faults;
            public IReadOnlyList<IReadOnlyList<Vector2Int>> PowerLoops;
        }

        private void UpdateChassisPowerAvailability(bool powerEnabled)
        {
            if (grid == null) return;

            for (int y = 0; y < grid.height; y++)
            {
                for (int x = 0; x < grid.width; x++)
                {
                    var cell = grid.GetCell(new Vector2Int(x, y));
                    if (cell.component == null || cell.component.def == null) continue;
                    if (cell.component.def.type != ComponentType.ChassisPowerConnection) continue;

                    grid.SetPower(new Vector2Int(x, y), powerEnabled);
                    cell.component.SetPowered(powerEnabled);
                }
            }
        }

        public struct LeakInfo
        {
            public Vector2Int Cell;
            public Vector3 WorldPosition;
        }
    }
}
