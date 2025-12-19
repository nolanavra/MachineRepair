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
        [Tooltip("Pressure lost per grid cell traveled by water flow.")]
        [SerializeField] private float pressureDropPerCell = 0.1f;
        [Tooltip("Whether the simulation loop is currently advancing.")]
        [SerializeField] private bool simulationRunning;
        [Header("Visualization")]
        [Tooltip("Seconds between spawning water flow arrows. Set to 0 to spawn every time flow advances.")]
        [SerializeField] private float waterArrowSpawnIntervalSeconds = 0.15f;
        [Header("Debugging")]
        [Tooltip("Log water flow arrow instantiation for debugging hydraulic paths.")]
        [SerializeField] private bool logWaterFlowPaths = false;

        private float stepTimer;
        private float waterArrowSpawnTimer;
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
        private readonly Dictionary<Vector2Int, PortElectricalState> portElectricalState = new();
        private readonly Dictionary<MachineComponent, float> componentFillLevels = new();
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

        public bool TryGetPortElectricalState(Vector2Int portCell, out PortElectricalState state)
        {
            return portElectricalState.TryGetValue(portCell, out state);
        }

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
            public Vector3[] Path;
            public float PathLength;
            public float TravelDistance;
            public float Speed;
            public float Scale;
            public Vector2Int StartCell;
            public Vector2Int EndCell;
        }

        private void Awake()
        {
            if (grid == null) grid = FindFirstObjectByType<GridManager>();

            ResetWaterArrowTimer();
        }

        private void Update()
        {
            if (waterOn)
            {
                waterArrowSpawnTimer += Time.deltaTime;
            }

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
            ResetWaterArrowTimer();
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
            ResetWaterArrowTimer();
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
                portElectricalState.Clear();
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
            portElectricalState.Clear();

            ApplyPoweredLoops(loops, portByCell, powerGraph.WireByEdge, portElectricalState);

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
                        if (port.portType != PortType.Power) continue;

                        Vector2Int global = cell.component.GetGlobalCell(port);
                        if (!grid.InBounds(global.x, global.y)) continue;

                        float voltage = Mathf.Max(cell.component.def.voltage, 0f);
                        float current = voltage > 0f && cell.component.def.wattage > 0f
                            ? cell.component.def.wattage / voltage
                            : 0f;

                        var powerPort = new PowerPort
                        {
                            Cell = global,
                            Component = cell.component,
                            PortIndex = i,
                            Port = port,
                            Voltage = voltage,
                            Current = current
                        };

                        portByCell[global] = powerPort;

                        if (cell.component.def.componentType == ComponentType.ChassisPowerConnection)
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
                var component = kvp.Key;
                var ports = kvp.Value;

                if (ports == null) continue;

                var portsByIndex = new Dictionary<int, PowerPort>();
                foreach (var port in ports)
                {
                    portsByIndex[port.PortIndex] = port;
                }

                foreach (var port in ports)
                {
                    foreach (var target in ResolveInternalConnections(port, portsByIndex))
                    {
                        if (!AllowsComponentConnection(component, port, target))
                        {
                            continue;
                        }

                        AddDirectionalEdge(port.Cell, target.Cell);
                        AddDirectionalEdge(target.Cell, port.Cell);
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

            IEnumerable<PowerPort> ResolveInternalConnections(
                PowerPort source,
                Dictionary<int, PowerPort> byIndex)
            {
                if (byIndex == null)
                {
                    yield break;
                }

                var indices = source.Port.internalConnectionIndices;
                if (indices != null && indices.Length > 0)
                {
                    for (int i = 0; i < indices.Length; i++)
                    {
                        int targetIndex = indices[i];
                        if (byIndex.TryGetValue(targetIndex, out var target))
                        {
                            yield return target;
                        }
                    }
                }
                else
                {
                    foreach (var target in byIndex.Values)
                    {
                        if (target.PortIndex == source.PortIndex) continue;
                        yield return target;
                    }
                }
            }

            bool AllowsComponentConnection(MachineComponent component, PowerPort from, PowerPort to)
            {
                if (component == null) return true;

                if (component.TryGetComponent<IConditionalPortLink>(out var conditional))
                {
                    return conditional.AllowsConnection(from.Port, from.PortIndex, to.Port, to.PortIndex);
                }

                return true;
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
            Dictionary<(Vector2Int, Vector2Int), PlacedWire> wireByEdge,
            Dictionary<Vector2Int, PortElectricalState> electricalByPort)
        {
            var componentPorts = new Dictionary<MachineComponent, ComponentPortState>();

            foreach (var loop in loops)
            {
                if (loop == null || loop.Count < 2) continue;

                float activeVoltage = 0f;
                float activeCurrent = 0f;

                for (int i = 0; i < loop.Count; i++)
                {
                    var node = loop[i];

                    if (portByCell.TryGetValue(node, out var port))
                    {
                        if (IsChassisPowerPort(port))
                        {
                            activeVoltage = Mathf.Max(activeVoltage, port.Voltage);
                            activeCurrent = Mathf.Max(activeCurrent, port.Current);
                        }

                        var electrical = new PortElectricalState
                        {
                            Voltage = activeVoltage,
                            Current = activeCurrent
                        };

                        MergePortElectricalState(node, electrical, electricalByPort);

                        if (port.Component != null)
                        {
                            if (!componentPorts.TryGetValue(port.Component, out var state))
                            {
                                state = new ComponentPortState();
                            }

                            state.Register(port, electrical);
                            componentPorts[port.Component] = state;
                        }
                    }

                    if (i < loop.Count - 1)
                    {
                        var next = loop[i + 1];

                        if (wireByEdge.TryGetValue((node, next), out var wire) || wireByEdge.TryGetValue((next, node), out wire))
                        {
                            if (activeVoltage > 0f || activeCurrent > 0f)
                            {
                                ApplyWirePower(wire, activeVoltage, activeCurrent);
                                poweredWires.Add(wire);
                            }
                        }
                    }
                }

                poweredLoops.Add(new List<Vector2Int>(loop));
            }

            foreach (var kvp in componentPorts)
            {
                var component = kvp.Key;
                var state = kvp.Value;

                if (component == null || component.def == null) continue;

                if (!state.HasInbound || !state.HasOutbound)
                {
                    if (component.def.power)
                    {
                        componentsMissingReturn.Add(component);
                    }

                    continue;
                }

                ApplyComponentPower(component, state.MaxVoltage, state.MaxCurrent);
            }
        }

        private static bool IsChassisPowerPort(PowerPort port)
        {
            return port.Component != null
                && port.Component.def != null
                && port.Component.def.componentType == ComponentType.ChassisPowerConnection;
        }

        private static void MergePortElectricalState(
            Vector2Int portCell,
            PortElectricalState electrical,
            Dictionary<Vector2Int, PortElectricalState> electricalByPort)
        {
            if (electricalByPort.TryGetValue(portCell, out var existing))
            {
                electrical.Voltage = Mathf.Max(existing.Voltage, electrical.Voltage);
                electrical.Current = Mathf.Max(existing.Current, electrical.Current);
            }

            electricalByPort[portCell] = electrical;
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
            public float MaxVoltage;
            public float MaxCurrent;

            public void Register(PowerPort port, PortElectricalState electrical)
            {
                if (electrical.Voltage <= 0f && electrical.Current <= 0f)
                {
                    return;
                }

                HasInbound = true;
                HasOutbound = true;

                MaxVoltage = Mathf.Max(MaxVoltage, electrical.Voltage);
                MaxCurrent = Mathf.Max(MaxCurrent, electrical.Current);
            }
        }

        public struct PortElectricalState
        {
            public float Voltage;
            public float Current;
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
            portElectricalState.Clear();

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
            public int PortIndex;
            public PortLocal Port;
            public float Voltage;
            public float Current;
        }

        private struct WaterPortRef
        {
            public Vector2Int Cell;
            public MachineComponent Component;
            public int PortIndex;
            public PortLocal Port;
        }

        private struct FlowFrontier
        {
            public WaterPortRef PortRef;
            public float AvailableFlow;
            public int StepsFromSource;
        }

        /// <summary>
        /// Propagates hydraulic flow starting at chassis water ports. Each port pushes its
        /// flowrate into connected pipes/components, filling them toward 100% before
        /// forwarding along internal port connections (pipe endpoints are treated as
        /// intra-component links once full). This maintains a deterministic, stepwise fill
        /// progression the UI can visualize.
        /// </summary>
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

            System.Array.Clear(pressureGraph, 0, pressureGraph.Length);
            System.Array.Clear(flowGraph, 0, flowGraph.Length);
            System.Array.Fill(waterArrowSteps, -1);

            CleanupComponentFillLevels();

            var waterPorts = new Dictionary<Vector2Int, WaterPortRef>();
            CollectWaterPorts(waterPorts);

            var processedPorts = new HashSet<Vector2Int>();
            var queue = new Queue<FlowFrontier>();

            foreach (var kvp in waterPorts)
            {
                var portRef = kvp.Value;
                if (portRef.Component?.def == null) continue;
                if (portRef.Component.def.componentType != ComponentType.ChassisWaterConnection) continue;

                SetComponentFill(portRef.Component, 100f);
                float sourceFlow = Mathf.Max(0f, portRef.Port.flowrateMax);
                float sourcePressure = portRef.Component.def.maxPressure;
                queue.Enqueue(new FlowFrontier
                {
                    PortRef = portRef,
                    AvailableFlow = sourceFlow,
                    StepsFromSource = 0
                });

                StampCellFlow(portRef.Cell, sourceFlow, CalculatePressureAfterTravel(sourcePressure, 0), 0);
            }

            while (queue.Count > 0)
            {
                var frontier = queue.Dequeue();

                if (!waterPorts.TryGetValue(frontier.PortRef.Cell, out var portRef)) continue;
                if (processedPorts.Contains(portRef.Cell)) continue;

                float availableFlow = Mathf.Min(frontier.AvailableFlow, Mathf.Max(0f, portRef.Port.flowrateMax));
                if (availableFlow <= 0f) continue;

                float componentFill = GetComponentFill(portRef.Component);
                float remainingComponent = Mathf.Max(0f, 100f - componentFill);
                if (remainingComponent > 0f)
                {
                    float applied = Mathf.Min(availableFlow, remainingComponent);
                    componentFill = Mathf.Min(100f, componentFill + applied);
                    SetComponentFill(portRef.Component, componentFill);
                    float appliedPressure = CalculatePressureAfterTravel(portRef.Component?.def?.maxPressure ?? 0f, frontier.StepsFromSource);
                    StampCellFlow(portRef.Cell, applied, appliedPressure, frontier.StepsFromSource);

                    if (componentFill < 100f - Mathf.Epsilon)
                    {
                        continue;
                    }
                }

                float availablePressure = CalculatePressureAfterTravel(portRef.Component?.def?.maxPressure ?? 0f, frontier.StepsFromSource);
                StampCellFlow(portRef.Cell, availableFlow, availablePressure, frontier.StepsFromSource);
                processedPorts.Add(portRef.Cell);

                bool propagatedToPipe = TryPushFlowIntoPipes(portRef, availableFlow, frontier.StepsFromSource, waterPorts, queue);
                if (!propagatedToPipe)
                {
                    RecordLeak(portRef.Cell);
                }

                PropagateInternalConnections(portRef, availableFlow, frontier.StepsFromSource, waterPorts, queue);
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

                        if (cell.component.def.power && componentsMissingReturn.Contains(cell.component))
                        {
                            faultLog.Add($"{cell.component.def.displayName} missing return path at {cell.component.anchorCell}");
                        }
                        else if (cell.component.def.power && !cell.component.IsPowered)
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
                PowerLoops = CloneLoops(poweredLoops),
                PortElectrical = new Dictionary<Vector2Int, PortElectricalState>(portElectricalState)
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

        private void CollectWaterPorts(Dictionary<Vector2Int, WaterPortRef> waterPorts)
        {
            waterPorts.Clear();

            for (int y = 0; y < grid.height; y++)
            {
                for (int x = 0; x < grid.width; x++)
                {
                    var cell = grid.GetCell(new Vector2Int(x, y));
                    if (cell.component == null || cell.component.portDef == null || cell.component.portDef.ports == null) continue;

                    for (int i = 0; i < cell.component.portDef.ports.Length; i++)
                    {
                        var port = cell.component.portDef.ports[i];
                        if (port.portType != PortType.Water) continue;

                        Vector2Int global = cell.component.GetGlobalCell(port);
                        if (grid.InBounds(global.x, global.y))
                        {
                            waterPorts[global] = new WaterPortRef
                            {
                                Cell = global,
                                Component = cell.component,
                                PortIndex = i,
                                Port = port
                            };
                        }
                    }
                }
            }
        }

        private void CleanupComponentFillLevels()
        {
            var toRemove = new List<MachineComponent>();
            foreach (var kvp in componentFillLevels)
            {
                if (kvp.Key == null)
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var component in toRemove)
            {
                componentFillLevels.Remove(component);
            }
        }

        private float GetComponentFill(MachineComponent component)
        {
            if (component == null) return 0f;
            return componentFillLevels.TryGetValue(component, out var fill)
                ? Mathf.Clamp(fill, 0f, 100f)
                : 0f;
        }

        private void SetComponentFill(MachineComponent component, float value)
        {
            if (component == null) return;

            componentFillLevels[component] = Mathf.Clamp(value, 0f, 100f);
        }

        private bool TryPushFlowIntoPipes(
            WaterPortRef portRef,
            float availableFlow,
            int stepsFromSource,
            IReadOnlyDictionary<Vector2Int, WaterPortRef> waterPorts,
            Queue<FlowFrontier> queue)
        {
            if (grid == null) return false;

            var cell = grid.GetCell(portRef.Cell);
            if (!cell.HasPipe) return false;

            bool propagated = false;
            foreach (var pipe in cell.Pipes)
            {
                if (pipe == null) continue;

                propagated = true;
                EnsurePipeFlowrate(pipe);
                var orderedCells = GetOrderedPipeCells(pipe, portRef.Cell);
                if (orderedCells.Count == 0) continue;

                float pipeRemaining = Mathf.Max(0f, 100f - Mathf.Clamp(pipe.fillLevel, 0f, 100f));
                float permitted = pipe.flowrateMax > 0f ? Mathf.Min(availableFlow, pipe.flowrateMax) : availableFlow;
                float applied = Mathf.Min(permitted, pipeRemaining);

                if (applied > 0f)
                {
                    pipe.fillLevel = Mathf.Min(100f, pipe.fillLevel + applied);
                    pipe.flow = applied;
                    StampPipeFlow(pipe, applied, orderedCells, stepsFromSource);

                    if (orderedCells.Count > 1 && ShouldSpawnWaterArrow())
                    {
                        int arrowSteps = stepsFromSource + CalculatePipeTraversalSteps(orderedCells);
                        AddWaterFlowArrowSegments(pipe, orderedCells, applied, arrowSteps);
                    }
                }

                if (pipe.fillLevel >= 100f - Mathf.Epsilon)
                {
                    EnqueueOppositePort(pipe, portRef.Cell, orderedCells, permitted, stepsFromSource, waterPorts, queue);
                }
            }

            return propagated;
        }

        private void RecordLeak(Vector2Int cell)
        {
            if (grid == null || !grid.InBounds(cell.x, cell.y)) return;

            for (int i = 0; i < detectedLeaks.Count; i++)
            {
                if (detectedLeaks[i].Cell == cell) return;
            }

            detectedLeaks.Add(new LeakInfo
            {
                Cell = cell,
                WorldPosition = grid.CellToWorld(cell)
            });
        }

        private void PropagateInternalConnections(
            WaterPortRef portRef,
            float availableFlow,
            int stepsFromSource,
            IReadOnlyDictionary<Vector2Int, WaterPortRef> waterPorts,
            Queue<FlowFrontier> queue)
        {
            var ports = portRef.Component?.portDef?.ports;
            if (ports == null) return;

            var connections = portRef.Port.internalConnectionIndices;
            if (connections == null) return;

            foreach (var targetIndex in connections)
            {
                if (targetIndex < 0 || targetIndex >= ports.Length) continue;

                var targetPort = ports[targetIndex];
                Vector2Int targetCell = portRef.Component.GetGlobalCell(targetPort);
                if (!grid.InBounds(targetCell.x, targetCell.y)) continue;
                if (!waterPorts.TryGetValue(targetCell, out var targetRef)) continue;

                float flowOut = Mathf.Min(availableFlow, Mathf.Max(0f, targetPort.flowrateMax));
                int nextSteps = stepsFromSource + 1;
                queue.Enqueue(new FlowFrontier
                {
                    PortRef = targetRef,
                    AvailableFlow = flowOut,
                    StepsFromSource = nextSteps
                });

                if (ShouldSpawnWaterArrow())
                {
                    float pressure = CalculatePressureAfterTravel(portRef.Component?.def?.maxPressure ?? 0f, nextSteps);
                    AddWaterFlowArrow(portRef.Cell, targetCell, flowOut, pressure, 1f, 0);
                }
            }
        }

        private static void EnsurePipeFlowrate(PlacedPipe pipe)
        {
            if (pipe == null) return;

            if (pipe.flowrateMax <= 0f && pipe.pipeDef != null)
            {
                pipe.flowrateMax = Mathf.Max(0f, pipe.pipeDef.maxFlow);
            }

            pipe.fillLevel = Mathf.Clamp(pipe.fillLevel, 0f, 100f);
        }

        private void StampCellFlow(Vector2Int cell, float flow, float pressure, int stepsFromSource)
        {
            if (grid == null || !grid.InBounds(cell.x, cell.y)) return;

            int idx = grid.ToIndex(cell);
            flowGraph[idx] = Mathf.Max(flowGraph[idx], flow);
            pressureGraph[idx] = Mathf.Max(pressureGraph[idx], pressure);

            if (waterArrowSteps[idx] == -1 || stepsFromSource < waterArrowSteps[idx])
            {
                waterArrowSteps[idx] = stepsFromSource;
            }
        }

        private void StampPipeFlow(PlacedPipe pipe, float flow, IReadOnlyList<Vector2Int> orderedCells, int stepsFromSource)
        {
            if (grid == null || pipe == null || orderedCells == null || orderedCells.Count == 0) return;

            float pressure = pipe.pipeDef != null ? pipe.pipeDef.maxPressure : pipe.pressure;
            int cumulativeSteps = stepsFromSource;

            for (int i = 0; i < orderedCells.Count; i++)
            {
                var cell = orderedCells[i];
                if (!grid.InBounds(cell.x, cell.y)) continue;

                if (i > 0)
                {
                    cumulativeSteps += GetStepDistance(orderedCells[i - 1], cell);
                }

                int idx = grid.ToIndex(cell);
                float droppedPressure = CalculatePressureAfterTravel(pressure, cumulativeSteps);
                flowGraph[idx] = Mathf.Max(flowGraph[idx], flow);
                pressureGraph[idx] = Mathf.Max(pressureGraph[idx], droppedPressure);

                if (waterArrowSteps[idx] == -1 || cumulativeSteps < waterArrowSteps[idx])
                {
                    waterArrowSteps[idx] = cumulativeSteps;
                }
            }
        }

        private void EnqueueOppositePort(
            PlacedPipe pipe,
            Vector2Int fromCell,
            IReadOnlyList<Vector2Int> orderedCells,
            float availableFlow,
            int stepsFromSource,
            IReadOnlyDictionary<Vector2Int, WaterPortRef> waterPorts,
            Queue<FlowFrontier> queue)
        {
            Vector2Int targetCell = fromCell == pipe.startPortCell ? pipe.endPortCell : pipe.startPortCell;
            int traversalSteps = stepsFromSource + CalculatePipeTraversalSteps(orderedCells);

            if (waterPorts.TryGetValue(targetCell, out var targetPort))
            {
                queue.Enqueue(new FlowFrontier
                {
                    PortRef = targetPort,
                    AvailableFlow = Mathf.Min(availableFlow, Mathf.Max(0f, targetPort.Port.flowrateMax)),
                    StepsFromSource = traversalSteps
                });

                if (ShouldSpawnWaterArrow())
                {
                    AddWaterFlowArrowSegments(pipe, orderedCells, availableFlow, traversalSteps);
                }
            }
            else
            {
                RecordLeak(targetCell);
            }
        }

        private void AddWaterFlowArrow(
            Vector2Int startCell,
            Vector2Int endCell,
            float flow,
            float pressure,
            float scaleMultiplier,
            float normalizedSpeed)
        {
            if (grid == null || startCell == endCell)
            {
                return;
            }

            Vector3 start = grid.CellToWorld(startCell);
            Vector3 end = grid.CellToWorld(endCell);
            float pathLength = Vector3.Distance(start, end);

            if (pathLength <= 0.0001f)
            {
                return;
            }

            float scale = Mathf.Max(0.001f, (pressure / 9f) * Mathf.Max(0.001f, scaleMultiplier));
            float speed = normalizedSpeed;

            if (speed <= 0f && flow > 0f)
            {
                speed = Mathf.Clamp01(flow / Mathf.Max(pressure, 1f));
            }

            var arrow = new WaterFlowArrow
            {
                StartCell = startCell,
                EndCell = endCell,
                Path = new[] { start, end },
                PathLength = pathLength,
                TravelDistance = Mathf.Max(0.001f, pathLength),
                Speed = Mathf.Max(0f, speed),
                Scale = scale
            };

            waterFlowArrows.Add(arrow);

            if (logWaterFlowPaths)
            {
                Debug.Log(
                    $"[SimulationManager] Added direct water arrow start={startCell} end={endCell} len={pathLength:0.###} speed={arrow.Speed:0.###} scale={arrow.Scale:0.###}");
            }
        }

        private List<Vector2Int> GetOrderedPipeCells(PlacedPipe pipe, Vector2Int originCell)
        {
            var orderedCells = new List<Vector2Int>(pipe?.occupiedCells ?? new List<Vector2Int>());
            if (orderedCells.Count == 0) return orderedCells;

            bool originIsStart = originCell == pipe?.startPortCell;
            bool originIsEnd = originCell == pipe?.endPortCell;

            if (!originIsStart && !originIsEnd)
            {
                if (logWaterFlowPaths)
                {
                    Debug.LogWarning($"[SimulationManager] Cannot order pipe cells: origin {originCell} is not part of pipe between {pipe?.startPortCell} and {pipe?.endPortCell}.");
                }

                orderedCells.Clear();
                return orderedCells;
            }

            if (originIsEnd)
            {
                orderedCells.Reverse();
            }

            int originIndex = orderedCells.IndexOf(originCell);
            if (originIndex > 0)
            {
                orderedCells.RemoveRange(0, originIndex);
            }
            else if (originIndex < 0)
            {
                orderedCells.Clear();
            }

            if (orderedCells.Count > 0 && orderedCells[0] != originCell && logWaterFlowPaths)
            {
                Debug.LogWarning($"[SimulationManager] Pipe occupancy ordering mismatch. Expected origin {originCell} at index 0 but found {orderedCells[0]}.");
            }

            return orderedCells;
        }

        private static int CalculatePipeTraversalSteps(IReadOnlyList<Vector2Int> orderedCells)
        {
            if (orderedCells == null || orderedCells.Count < 2) return 0;

            int steps = 0;
            for (int i = 1; i < orderedCells.Count; i++)
            {
                steps += GetStepDistance(orderedCells[i - 1], orderedCells[i]);
            }

            return steps;
        }

        private void AddWaterFlowArrowSegments(PlacedPipe pipe, IReadOnlyList<Vector2Int> orderedCells, float flow, int stepsFromSource)
        {
            if (grid == null || pipe == null || orderedCells == null || orderedCells.Count < 2) return;

            EnsurePipeFlowrate(pipe);

            float pressure = CalculatePressureAfterTravel(pipe.pipeDef != null ? pipe.pipeDef.maxPressure : pipe.pressure, stepsFromSource);
            float normalizedSpeed = pipe.flowrateMax > 0f ? Mathf.Clamp(flow / pipe.flowrateMax, 0f, 1f) : 0f;
            float pathLength = 0f;

            var path = new List<Vector3>(orderedCells.Count);
            for (int i = 0; i < orderedCells.Count; i++)
            {
                Vector3 point = grid.CellToWorld(orderedCells[i]);
                path.Add(point);

                if (i == 0) continue;

                Vector3 prev = path[i - 1];
                float segmentLength = Vector3.Distance(prev, point);
                if (Mathf.Abs(segmentLength) < 0.0001f)
                {
                    continue;
                }

                pathLength += segmentLength;
                if (Mathf.Abs(orderedCells[i - 1].x - orderedCells[i].x) > 1 || Mathf.Abs(orderedCells[i - 1].y - orderedCells[i].y) > 1)
                {
                    if (logWaterFlowPaths)
                    {
                        Debug.LogWarning($"[SimulationManager] Non-adjacent pipe segment detected between {orderedCells[i - 1]} and {orderedCells[i]} (segment {i - 1}).");
                    }
                }
            }

            if (path.Count < 2 || pathLength <= 0.0001f)
            {
                return;
            }

            float travelDistance = pipe.fillLevel >= 100f - Mathf.Epsilon
                ? pathLength
                : pathLength * 0.5f;

            var arrow = new WaterFlowArrow
            {
                StartCell = orderedCells[0],
                EndCell = orderedCells[^1],
                Path = path.ToArray(),
                PathLength = pathLength,
                TravelDistance = Mathf.Max(0.001f, travelDistance),
                Speed = Mathf.Max(0f, normalizedSpeed),
                Scale = pressure / 9f
            };

            waterFlowArrows.Add(arrow);

            if (logWaterFlowPaths)
            {
                Debug.Log($"[SimulationManager] Added water arrow path start={arrow.StartCell} end={arrow.EndCell} len={arrow.PathLength:0.###} travel={arrow.TravelDistance:0.###} speed={arrow.Speed:0.###} scale={arrow.Scale:0.###}");
            }
        }

        private bool ShouldSpawnWaterArrow()
        {
            if (waterArrowSpawnIntervalSeconds <= 0f)
            {
                return true;
            }

            if (waterArrowSpawnTimer >= waterArrowSpawnIntervalSeconds)
            {
                waterArrowSpawnTimer = 0f;
                return true;
            }

            return false;
        }

        private void ResetWaterArrowTimer()
        {
            waterArrowSpawnTimer = Mathf.Max(waterArrowSpawnIntervalSeconds, 0f);
        }

        private float CalculatePressureAfterTravel(float sourcePressure, int stepsTraveled)
        {
            if (pressureDropPerCell <= 0f) return sourcePressure;

            float dropped = sourcePressure - pressureDropPerCell * Mathf.Max(0, stepsTraveled);
            return Mathf.Max(0f, dropped);
        }

        private static int GetStepDistance(Vector2Int origin, Vector2Int destination)
        {
            int dx = Mathf.Abs(destination.x - origin.x);
            int dy = Mathf.Abs(destination.y - origin.y);
            return Mathf.Max(dx, dy);
        }

        private bool IsWaterSupplyComponent(cellDef cell)
        {
            return cell.component != null
                && cell.component.def != null
                && cell.component.def.componentType == ComponentType.ChassisWaterConnection;
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
            public IReadOnlyDictionary<Vector2Int, PortElectricalState> PortElectrical;

            public bool TryGetPortElectricalState(Vector2Int portCell, out PortElectricalState state)
            {
                if (PortElectrical != null && PortElectrical.TryGetValue(portCell, out var electrical))
                {
                    state = electrical;
                    return true;
                }

                state = default;
                return false;
            }
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
                    if (cell.component.def.componentType != ComponentType.ChassisPowerConnection) continue;

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
