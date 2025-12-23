using System;
using System.Collections.Generic;
using MachineRepair.Grid;
using MachineRepair;
using UnityEngine;

namespace MachineRepair.Fluid
{
    /// <summary>
    /// Bridges the gameplay grid to the hydraulic solver. Builds junction nodes at every
    /// water port, adds edges for pipes and internal connections, then solves each connected
    /// component with the allocation-free <see cref="HydraulicNetworkSolver"/>.
    /// </summary>
    public sealed class HydraulicSystem
    {
        public struct PortHydraulicState
        {
            public float Pressure_Pa;
            public float Flow_m3s;
            public bool IsSource;
            public float SourcePressure_Pa;
        }

        public struct PipeRuntimeState
        {
            public Vector2Int StartCell;
            public Vector2Int EndCell;
            public float Volume_m3;
            public float Fill01;
            public float Flow_m3s;
            public float InletPressure_Pa;
            public float OutletPressure_Pa;

            public float FillPercent => Fill01 * 100f;
        }

        public struct ComponentRuntimeState
        {
            public int ComponentId;
            public Vector2Int AnchorCell;
            public float Volume_m3;
            public float Fill01;
            public float NetFlowIn_m3s;
            public float RepresentativePressure_Pa;

            public float FillPercent => Fill01 * 100f;
            public float CurrentVolume_m3 => Volume_m3 * Fill01;
        }

        private sealed class HydraulicSubgraph
        {
            public readonly List<int> NodeIndices = new();
            public readonly List<int> EdgeIndices = new();
            public readonly Dictionary<int, int> LocalNodeLookup = new();
        }

        private sealed class HydraulicEdgeBinding
        {
            public int EdgeIndex = -1;
            public PlacedPipe Pipe;
            public Vector2Int StartCell;
            public Vector2Int EndCell;
            public IReadOnlyList<Vector2Int> OccupiedCells;
            public Vector3[] Path;
            public Vector3[] ReversePath;
            public float PathLength;
            public double Volume_m3;
        }

        private readonly GridManager grid;
        private readonly HydraulicNetworkSolver solver = new();
        private HydraulicSolverSettings settings;
        private float stepDuration = 0.1f;

        private readonly List<HydraulicNode> nodes = new();
        private readonly List<HydraulicEdge> edges = new();
        private readonly List<HydraulicSubgraph> subgraphs = new();
        private readonly List<HydraulicNode> scratchNodes = new();
        private readonly List<HydraulicEdge> scratchEdges = new();
        private readonly List<int> scratchEdgeGlobalA = new();
        private readonly List<int> scratchEdgeGlobalB = new();
        private readonly Dictionary<int, int> scratchLookup = new();
        private readonly Dictionary<Vector2Int, int> nodeIndexByCell = new();
        private readonly Dictionary<int, List<PortReference>> portsByNode = new();
        private readonly Dictionary<Vector2Int, float> portPressureByCell = new();
        private readonly Dictionary<Vector2Int, FlowAccumulator> portFlowByCell = new();
        private readonly Dictionary<Vector2Int, bool> sourceByCell = new();
        private readonly Dictionary<Vector2Int, PortHydraulicState> portStatesByCell = new();
        private readonly Dictionary<Vector2Int, float> pipeDeltaPByCell = new();
        private readonly List<HydraulicEdgeBinding> pipeBindings = new();
        private readonly Dictionary<PlacedPipe, PipeRuntimeState> pipeRuntimeStateByPipe = new();
        private readonly Dictionary<int, PipeRuntimeState> pipeRuntimeStateById = new();
        private readonly Dictionary<MachineComponent, ComponentRuntimeState> componentRuntimeStateByComponent = new();
        private readonly Dictionary<int, ComponentRuntimeState> componentRuntimeStateById = new();
        private readonly Dictionary<Vector2Int, ComponentRuntimeState> componentRuntimeStateByCell = new();
        private readonly Dictionary<MachineComponent, float> componentNetFlow = new();
        private readonly Dictionary<MachineComponent, float> componentPressure = new();
        private readonly HashSet<MachineComponent> activeWaterComponents = new();
        private readonly List<MachineComponent> componentRemovalBuffer = new();
        private readonly List<SimulationManager.WaterFlowArrow> arrowBuffer = new();

        private float[] pressureField = Array.Empty<float>();
        private float[] flowField = Array.Empty<float>();

        private bool dirty = true;
        private bool waterEnabled;

        private const double DefaultDiameter_m = 0.01d;
        private const double DefaultRoughness_m = 1e-5d;
        private const double DefaultDensity_kgm3 = 998d;
        private const double DefaultViscosity_Pa_s = 1.002e-3d;
        private const float LitersToCubicMeters = 0.001f;

        public HydraulicSystem(GridManager grid, HydraulicSolverSettings? overrideSettings = null)
        {
            this.grid = grid;
            settings = overrideSettings ?? HydraulicSolverSettings.Default;
            if (grid != null)
            {
                grid.GridTopologyChanged += OnGridChanged;
            }
        }

        public IReadOnlyList<HydraulicNode> Nodes => nodes;
        public IReadOnlyList<HydraulicEdge> Edges => edges;
        public IReadOnlyDictionary<Vector2Int, PortHydraulicState> PortStates => portStatesByCell;
        public IReadOnlyDictionary<Vector2Int, bool> SourceByCell => sourceByCell;
        public IReadOnlyDictionary<Vector2Int, float> PipeDeltaPByCell => pipeDeltaPByCell;
        public IReadOnlyDictionary<PlacedPipe, PipeRuntimeState> PipeRuntimeStates => pipeRuntimeStateByPipe;
        public IReadOnlyDictionary<int, PipeRuntimeState> PipeRuntimeStatesById => pipeRuntimeStateById;
        public IReadOnlyDictionary<int, ComponentRuntimeState> ComponentRuntimeStatesById => componentRuntimeStateById;
        public IReadOnlyDictionary<Vector2Int, ComponentRuntimeState> ComponentRuntimeStatesByCell => componentRuntimeStateByCell;
        public float[] PressureField => pressureField;
        public float[] FlowField => flowField;
        public IReadOnlyList<SimulationManager.WaterFlowArrow> FlowArrows => arrowBuffer;
        public int ConnectedComponentCount => subgraphs.Count;

        public bool TryGetComponentNodeIndices(int componentIndex, out IReadOnlyList<int> nodeIndices)
        {
            if (componentIndex >= 0 && componentIndex < subgraphs.Count)
            {
                nodeIndices = subgraphs[componentIndex].NodeIndices;
                return true;
            }

            nodeIndices = Array.Empty<int>();
            return false;
        }

        public void SetWaterEnabled(bool enabled)
        {
            waterEnabled = enabled;
        }

        public void SetStepDuration(float durationSeconds)
        {
            stepDuration = Mathf.Max(1e-4f, durationSeconds);
        }

        public void MarkDirty()
        {
            dirty = true;
        }

        public HydraulicSolveResult Solve()
        {
            if (grid == null || !waterEnabled)
            {
                ClearOutput();
                return default;
            }

            if (dirty)
            {
                Rebuild();
            }

            EnsureFields();
            ClearOutput();

            double maxResidual = 0d;
            int iterations = 0;
            bool converged = true;

            for (int i = 0; i < subgraphs.Count; i++)
            {
                var result = SolveSubgraph(subgraphs[i]);
                maxResidual = Math.Max(maxResidual, result.MaxResidual);
                iterations = Math.Max(iterations, result.Iterations);
                converged &= result.Converged;
            }

            StampFields();
            BuildPortStates();
            UpdateComponentRuntimeStates();
            BuildFlowArrows();

            return new HydraulicSolveResult
            {
                Converged = converged,
                Iterations = iterations,
                MaxResidual = maxResidual
            };
        }

        private void OnGridChanged()
        {
            MarkDirty();
        }

        private void Rebuild()
        {
            dirty = false;
            nodes.Clear();
            edges.Clear();
            subgraphs.Clear();
            nodeIndexByCell.Clear();
            portsByNode.Clear();
            pipeBindings.Clear();

            BuildNodes();
            PruneComponentRuntimeStates();
            BuildEdges();
            BuildSubgraphs();
        }

        private void BuildNodes()
        {
            if (grid == null) return;

            activeWaterComponents.Clear();

            for (int y = 0; y < grid.height; y++)
            {
                for (int x = 0; x < grid.width; x++)
                {
                    var cell = grid.GetCell(new Vector2Int(x, y));
                    var component = cell.component;
                    if (component == null || component.portDef == null) continue;
                    if (component.portDef.ports == null || component.portDef.ports.Length == 0) continue;
                    if (component.def == null || !component.def.water) continue;

                    activeWaterComponents.Add(component);

                    for (int i = 0; i < component.portDef.ports.Length; i++)
                    {
                        var port = component.portDef.ports[i];
                        if (port.portType != PortType.Water) continue;

                        Vector2Int global = component.GetGlobalCell(port);
                        if (!grid.InBounds(global.x, global.y)) continue;

                        bool isSource = IsHydraulicSupply(component);
                        double pressure = isSource ? ResolveSourcePressure(component) : 0d;
                        bool isFixed = isSource && pressure > 0d;
                        int nodeIndex = GetOrCreateNode(global, pressure, isFixed, isSource);

                        if (!portsByNode.TryGetValue(nodeIndex, out var list))
                        {
                            list = new List<PortReference>();
                            portsByNode[nodeIndex] = list;
                        }

                        list.Add(new PortReference(component, i));
                    }
                }
            }
        }

        private void BuildEdges()
        {
            var internalKeys = new HashSet<(int, int)>();
            BuildInternalConnections(internalKeys);
            var pipes = CollectPipes();
            BuildPipeConnections(pipes);
            PrunePipeRuntimeState(pipes);
        }

        private void BuildInternalConnections(HashSet<(int, int)> connectionKeys)
        {
            if (grid == null) return;

            for (int y = 0; y < grid.height; y++)
            {
                for (int x = 0; x < grid.width; x++)
                {
                    var cell = grid.GetCell(new Vector2Int(x, y));
                    var component = cell.component;
                    if (component == null || component.portDef == null || component.portDef.ports == null) continue;
                    if (component.def == null || !component.def.water) continue;

                    var ports = component.portDef.ports;
                    for (int i = 0; i < ports.Length; i++)
                    {
                        var port = ports[i];
                        if (port.portType != PortType.Water || port.internalConnectionIndices == null) continue;

                        Vector2Int fromCell = component.GetGlobalCell(port);
                        if (!nodeIndexByCell.TryGetValue(fromCell, out var fromIndex)) continue;

                        for (int j = 0; j < port.internalConnectionIndices.Length; j++)
                        {
                            int targetIndex = port.internalConnectionIndices[j];
                            if (targetIndex < 0 || targetIndex >= ports.Length) continue;

                            var targetPort = ports[targetIndex];
                            if (targetPort.portType != PortType.Water) continue;

                            Vector2Int toCell = component.GetGlobalCell(targetPort);
                            if (!nodeIndexByCell.TryGetValue(toCell, out var toIndex)) continue;

                            int keyA = Math.Min(fromIndex, toIndex);
                            int keyB = Math.Max(fromIndex, toIndex);
                            var key = (keyA, keyB);
                            if (connectionKeys.Contains(key)) continue;
                            connectionKeys.Add(key);

                            double maxFlow = Math.Min(Math.Max(0d, port.flowrateMax), Math.Max(0d, targetPort.flowrateMax));
                            var element = new LinearResistanceElementModel(resistance_PaPer_m3s: 1d, maxFlow_m3s: maxFlow);
                            var edge = new HydraulicEdge
                            {
                                Id = edges.Count,
                                NodeA = fromIndex,
                                NodeB = toIndex,
                                Model = element,
                                Kind = HydraulicEdgeKind.InternalConnector
                            };

                            edges.Add(edge);
                        }
                    }
                }
            }
        }

        private void BuildPipeConnections(List<PlacedPipe> pipes)
        {
            if (grid == null || pipes == null) return;

            foreach (var pipe in pipes)
            {
                if (pipe == null) continue;

                if (!nodeIndexByCell.TryGetValue(pipe.startPortCell, out var startIndex)) continue;
                if (!nodeIndexByCell.TryGetValue(pipe.endPortCell, out var endIndex)) continue;

                var binding = BuildPipeBinding(pipe);
                var element = BuildPipeModel(pipe);

                var edge = new HydraulicEdge
                {
                    Id = edges.Count,
                    NodeA = startIndex,
                    NodeB = endIndex,
                    Model = element,
                    Kind = HydraulicEdgeKind.Pipe,
                    Tag = binding
                };

                binding.EdgeIndex = edges.Count;
                edges.Add(edge);
                pipeBindings.Add(binding);
            }
        }

        private HydraulicEdgeBinding BuildPipeBinding(PlacedPipe pipe)
        {
            var binding = new HydraulicEdgeBinding
            {
                Pipe = pipe,
                StartCell = pipe.startPortCell,
                EndCell = pipe.endPortCell,
                OccupiedCells = pipe.occupiedCells
            };

            if (grid == null)
            {
                return binding;
            }

            var path = new List<Vector3>(pipe.occupiedCells.Count);
            float pathLength = 0f;
            for (int i = 0; i < pipe.occupiedCells.Count; i++)
            {
                var world = grid.CellToWorld(pipe.occupiedCells[i]);
                path.Add(world);

                if (i > 0)
                {
                    pathLength += Vector3.Distance(path[i - 1], path[i]);
                }
            }

            binding.Path = path.ToArray();
            binding.PathLength = pathLength;
            binding.Volume_m3 = CalculatePipeVolume(pipe);

            path.Reverse();
            binding.ReversePath = path.ToArray();

            return binding;
        }

        private PipeElementModel BuildPipeModel(PlacedPipe pipe)
        {
            double length = CalculatePipeLength(pipe);
            double diameter = ResolvePipeDiameter(pipe);
            double roughness = DefaultRoughness_m;
            double density = DefaultDensity_kgm3;
            double viscosity = DefaultViscosity_Pa_s;
            double flowCap = pipe.flowrateMax;
            double[] minor = null;

            if (pipe.pipeDef != null)
            {
                diameter = Math.Max(DefaultDiameter_m, pipe.pipeDef.innerDiameter_m);
                roughness = Math.Max(0d, pipe.pipeDef.roughness_m);
                density = Math.Max(DefaultDensity_kgm3, pipe.pipeDef.fluidDensity_kgm3);
                viscosity = Math.Max(DefaultViscosity_Pa_s, pipe.pipeDef.fluidViscosity_PaS);
                flowCap = pipe.pipeDef.maxFlow > 0f ? pipe.pipeDef.maxFlow : pipe.flowrateMax;
                if (pipe.pipeDef.minorLosses != null && pipe.pipeDef.minorLosses.Length > 0)
                {
                    minor = ConvertMinorLosses(pipe.pipeDef.minorLosses);
                }
            }

            return new PipeElementModel(length, diameter, roughness, minor, density, viscosity, flowCap);
        }

        private void BuildSubgraphs()
        {
            if (nodes.Count == 0 || edges.Count == 0)
            {
                return;
            }

            var componentByNode = new int[nodes.Count];
            for (int i = 0; i < componentByNode.Length; i++)
            {
                componentByNode[i] = -1;
            }

            int componentId = 0;
            var queue = new Queue<int>();

            for (int i = 0; i < nodes.Count; i++)
            {
                if (componentByNode[i] >= 0) continue;

                var subgraph = new HydraulicSubgraph();
                var edgeSet = new HashSet<int>();
                queue.Enqueue(i);
                componentByNode[i] = componentId;

                while (queue.Count > 0)
                {
                    int nodeIndex = queue.Dequeue();
                    subgraph.NodeIndices.Add(nodeIndex);

                    for (int e = 0; e < edges.Count; e++)
                    {
                        var edge = edges[e];
                        if (edge.NodeA != nodeIndex && edge.NodeB != nodeIndex) continue;
                        if (edgeSet.Add(e))
                        {
                            subgraph.EdgeIndices.Add(e);
                        }

                        int other = edge.NodeA == nodeIndex ? edge.NodeB : edge.NodeA;
                        if (componentByNode[other] >= 0) continue;

                        componentByNode[other] = componentId;
                        queue.Enqueue(other);
                    }
                }

                for (int n = 0; n < subgraph.NodeIndices.Count; n++)
                {
                    subgraph.LocalNodeLookup[subgraph.NodeIndices[n]] = n;
                }

                subgraphs.Add(subgraph);
                componentId++;
            }
        }

        private HydraulicSolveResult SolveSubgraph(HydraulicSubgraph subgraph)
        {
            scratchNodes.Clear();
            scratchEdges.Clear();
            scratchEdgeGlobalA.Clear();
            scratchEdgeGlobalB.Clear();
            scratchLookup.Clear();

            for (int i = 0; i < subgraph.NodeIndices.Count; i++)
            {
                int nodeIndex = subgraph.NodeIndices[i];
                scratchLookup[nodeIndex] = i;
                scratchNodes.Add(nodes[nodeIndex]);
            }

            for (int i = 0; i < subgraph.EdgeIndices.Count; i++)
            {
                int edgeIndex = subgraph.EdgeIndices[i];
                var edge = edges[edgeIndex];

                scratchEdgeGlobalA.Add(edge.NodeA);
                scratchEdgeGlobalB.Add(edge.NodeB);

                edge.NodeA = scratchLookup[edge.NodeA];
                edge.NodeB = scratchLookup[edge.NodeB];
                scratchEdges.Add(edge);
            }

            var result = solver.Solve(scratchNodes, scratchEdges, settings);
            ApplySolvedNodes(subgraph.NodeIndices, scratchNodes);
            ApplySolvedEdges(subgraph.EdgeIndices, scratchEdges, scratchEdgeGlobalA, scratchEdgeGlobalB);

            return result;
        }

        private void ApplySolvedNodes(IReadOnlyList<int> indices, IReadOnlyList<HydraulicNode> solved)
        {
            for (int i = 0; i < indices.Count && i < solved.Count; i++)
            {
                nodes[indices[i]] = solved[i];
            }
        }

        private void ApplySolvedEdges(
            IReadOnlyList<int> edgeIndices,
            IReadOnlyList<HydraulicEdge> solved,
            IReadOnlyList<int> globalA,
            IReadOnlyList<int> globalB)
        {
            for (int i = 0; i < edgeIndices.Count && i < solved.Count; i++)
            {
                int edgeIndex = edgeIndices[i];
                var edge = edges[edgeIndex];
                edge.Flow_m3s = solved[i].Flow_m3s;
                edge.LastDeltaP_Pa = solved[i].LastDeltaP_Pa;
                edge.NodeA = globalA[i];
                edge.NodeB = globalB[i];
                edges[edgeIndex] = edge;
            }
        }

        private void ClearOutput()
        {
            if (pressureField != null)
            {
                Array.Clear(pressureField, 0, pressureField.Length);
            }

            if (flowField != null)
            {
                Array.Clear(flowField, 0, flowField.Length);
            }

            portPressureByCell.Clear();
            portFlowByCell.Clear();
            sourceByCell.Clear();
            portStatesByCell.Clear();
            pipeDeltaPByCell.Clear();
            arrowBuffer.Clear();
            pipeRuntimeStateById.Clear();
            componentRuntimeStateById.Clear();
            componentRuntimeStateByCell.Clear();
            ResetPipeRuntimeSamples();
        }

        private void StampFields()
        {
            if (grid == null) return;

            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                int idx = grid.ToIndex(node.Cell);
                if (idx < 0 || idx >= pressureField.Length) continue;

                float pressure = (float)Math.Max(0d, node.Pressure_Pa);
                pressureField[idx] = Math.Max(pressureField[idx], pressure);
                portPressureByCell[node.Cell] = pressure;
                sourceByCell[node.Cell] = node.IsSource;
            }

            for (int i = 0; i < edges.Count; i++)
            {
                var edge = edges[i];
                float flow = (float)edge.Flow_m3s;
                float deltaP = (float)Math.Abs(edge.LastDeltaP_Pa);

                var nodeA = nodes[edge.NodeA];
                var nodeB = nodes[edge.NodeB];

                AccumulatePortFlow(nodeA.Cell, flow);
                AccumulatePortFlow(nodeB.Cell, -flow);

                if (edge.Tag is HydraulicEdgeBinding binding)
                {
                    StampPipeFlow(binding, Math.Abs(flow));
                    StampPipeDeltaP(binding, deltaP);
                    UpdatePipeRuntimeState(binding, edge, nodeA, nodeB);
                }
            }
        }

        private void StampPipeFlow(HydraulicEdgeBinding binding, float flow)
        {
            if (grid == null || binding.OccupiedCells == null) return;

            for (int i = 0; i < binding.OccupiedCells.Count; i++)
            {
                var cell = binding.OccupiedCells[i];
                if (!grid.InBounds(cell.x, cell.y)) continue;

                int idx = grid.ToIndex(cell);
                if (idx < 0 || idx >= flowField.Length) continue;

                flowField[idx] = Math.Max(flowField[idx], flow);
            }
        }

        private void StampPipeDeltaP(HydraulicEdgeBinding binding, float deltaP)
        {
            if (grid == null || binding.OccupiedCells == null) return;

            for (int i = 0; i < binding.OccupiedCells.Count; i++)
            {
                var cell = binding.OccupiedCells[i];
                if (!grid.InBounds(cell.x, cell.y)) continue;

                if (!pipeDeltaPByCell.TryGetValue(cell, out var existing) || deltaP > existing)
                {
                    pipeDeltaPByCell[cell] = deltaP;
                }
            }
        }

        private void UpdatePipeRuntimeState(
            HydraulicEdgeBinding binding,
            HydraulicEdge edge,
            HydraulicNode nodeA,
            HydraulicNode nodeB)
        {
            if (binding == null) return;

            double flow = edge.Flow_m3s;
            bool forward = flow >= 0d;
            var inletNode = forward ? nodeA : nodeB;
            var outletNode = forward ? nodeB : nodeA;

            Vector2Int inletCell = forward ? binding.StartCell : binding.EndCell;
            Vector2Int outletCell = forward ? binding.EndCell : binding.StartCell;

            double inletPressure = Math.Max(0d, inletNode.Pressure_Pa);
            double directionalDeltaP = forward ? edge.LastDeltaP_Pa : -edge.LastDeltaP_Pa;
            double outletPressure = Math.Max(0d, inletPressure - Math.Abs(directionalDeltaP));

            UpdatePortPressure(inletCell, (float)inletPressure);
            UpdatePortPressure(outletCell, (float)outletPressure);

            if (binding.Pipe == null) return;

            var state = GetOrCreatePipeRuntimeState(binding, inletPressure, outletPressure, flow);
            binding.Pipe.flow = (float)flow;
            binding.Pipe.fillLevel = state.FillPercent;
            binding.Pipe.pressure = Mathf.Max(state.InletPressure_Pa, state.OutletPressure_Pa);
        }

        private void UpdatePortPressure(Vector2Int cell, float pressure)
        {
            if (grid == null || !grid.InBounds(cell.x, cell.y)) return;
            pressure = Mathf.Max(0f, pressure);

            if (portPressureByCell.TryGetValue(cell, out var existing))
            {
                portPressureByCell[cell] = Math.Max(existing, pressure);
            }
            else
            {
                portPressureByCell[cell] = pressure;
            }
        }

        private PipeRuntimeState GetOrCreatePipeRuntimeState(
            HydraulicEdgeBinding binding,
            double inletPressure,
            double outletPressure,
            double flow_m3s)
        {
            var pipe = binding.Pipe;
            if (pipe == null) return default;

            if (!pipeRuntimeStateByPipe.TryGetValue(pipe, out var state))
            {
                state = new PipeRuntimeState
                {
                    StartCell = binding.StartCell,
                    EndCell = binding.EndCell,
                    Fill01 = Mathf.Clamp01(pipe.fillLevel / 100f)
                };
            }

            double volume = binding.Volume_m3 > 0d ? binding.Volume_m3 : CalculatePipeVolume(pipe);
            state.Volume_m3 = (float)volume;
            state.StartCell = binding.StartCell;
            state.EndCell = binding.EndCell;

            double currentFill = Math.Min(1d, Math.Max(0d, state.Fill01));
            double filledVolume = volume * currentFill;
            double deltaVolume = flow_m3s * stepDuration;
            double nextVolume = filledVolume + deltaVolume;

            if (nextVolume < 0d) nextVolume = 0d;
            if (nextVolume > volume) nextVolume = volume;

            state.Fill01 = volume > 0d ? (float)(nextVolume / volume) : 0f;
            state.Flow_m3s = (float)flow_m3s;
            state.InletPressure_Pa = (float)inletPressure;
            state.OutletPressure_Pa = (float)outletPressure;

            pipeRuntimeStateByPipe[pipe] = state;
            pipeRuntimeStateById[pipe.GetInstanceID()] = state;

            return state;
        }

        private void AccumulatePortFlow(Vector2Int cell, float contribution)
        {
            if (!portFlowByCell.TryGetValue(cell, out var accumulator))
            {
                accumulator = default;
            }

            accumulator.Add(contribution);
            portFlowByCell[cell] = accumulator;
        }

        private void BuildPortStates()
        {
            foreach (var kvp in portPressureByCell)
            {
                var cell = kvp.Key;
                float pressure = kvp.Value;
                bool isSource = sourceByCell.TryGetValue(cell, out var fromStamp) && fromStamp;
                float sourcePressure = 0f;

                if (nodeIndexByCell.TryGetValue(cell, out var nodeIndex) && nodeIndex >= 0 && nodeIndex < nodes.Count)
                {
                    var node = nodes[nodeIndex];
                    isSource = node.IsSource;
                    sourcePressure = (float)Math.Max(0d, node.SourcePressure_Pa > 0d ? node.SourcePressure_Pa : node.FixedPressure_Pa);
                    sourceByCell[cell] = isSource;
                }

                float flow = 0f;
                if (portFlowByCell.TryGetValue(cell, out var accumulator))
                {
                    flow = accumulator.ResolveDisplayFlow();
                }

                portStatesByCell[cell] = new PortHydraulicState
                {
                    Pressure_Pa = pressure,
                    Flow_m3s = flow,
                    IsSource = isSource,
                    SourcePressure_Pa = sourcePressure
                };
            }
        }

        private void UpdateComponentRuntimeStates()
        {
            componentRuntimeStateById.Clear();
            componentRuntimeStateByCell.Clear();
            componentNetFlow.Clear();
            componentPressure.Clear();

            if (grid == null) return;

            foreach (var kvp in portsByNode)
            {
                int nodeIndex = kvp.Key;
                if (nodeIndex < 0 || nodeIndex >= nodes.Count) continue;
                var node = nodes[nodeIndex];

                for (int i = 0; i < kvp.Value.Count; i++)
                {
                    var portRef = kvp.Value[i];
                    var component = portRef.Component;
                    if (component == null || component.def == null || !component.def.water) continue;

                    float netFlow = 0f;
                    if (portFlowByCell.TryGetValue(node.Cell, out var accumulator))
                    {
                        netFlow = accumulator.Net;
                    }

                    if (componentNetFlow.TryGetValue(component, out var existingFlow))
                    {
                        componentNetFlow[component] = existingFlow + netFlow;
                    }
                    else
                    {
                        componentNetFlow[component] = netFlow;
                    }

                    float pressure = 0f;
                    if (portPressureByCell.TryGetValue(node.Cell, out var portPressure))
                    {
                        pressure = portPressure;
                    }

                    if (componentPressure.TryGetValue(component, out var existingPressure))
                    {
                        componentPressure[component] = Math.Max(existingPressure, pressure);
                    }
                    else
                    {
                        componentPressure[component] = pressure;
                    }
                }
            }

            foreach (var kvp in componentNetFlow)
            {
                var component = kvp.Key;
                float netFlow = kvp.Value;
                var state = GetOrCreateComponentRuntimeState(component);
                state.Volume_m3 = ResolveComponentVolume_m3(component);

                float capacity_m3 = state.Volume_m3;
                double currentVolume = Math.Max(0d, capacity_m3 * state.Fill01);
                double nextVolume = currentVolume - (double)netFlow * stepDuration;

                if (capacity_m3 > 0d)
                {
                    nextVolume = Math.Min(capacity_m3, Math.Max(0d, nextVolume));
                    state.Fill01 = (float)(nextVolume / capacity_m3);
                }
                else
                {
                    state.Fill01 = 0f;
                }

                state.AnchorCell = component != null ? component.anchorCell : state.AnchorCell;
                state.ComponentId = component != null ? component.GetInstanceID() : state.ComponentId;
                state.NetFlowIn_m3s = -netFlow;
                state.RepresentativePressure_Pa = componentPressure.TryGetValue(component, out var portPressure) ? portPressure : 0f;

                CommitComponentRuntimeState(component, state);
            }
        }

        private ComponentRuntimeState GetOrCreateComponentRuntimeState(MachineComponent component)
        {
            if (component != null && componentRuntimeStateByComponent.TryGetValue(component, out var state))
            {
                return state;
            }

            float capacity_m3 = ResolveComponentVolume_m3(component);
            float seedFill = ResolveSeedFill01(component);

            state = new ComponentRuntimeState
            {
                ComponentId = component != null ? component.GetInstanceID() : 0,
                AnchorCell = component != null ? component.anchorCell : default,
                Volume_m3 = capacity_m3,
                Fill01 = seedFill,
                NetFlowIn_m3s = 0f,
                RepresentativePressure_Pa = 0f
            };

            if (component != null)
            {
                componentRuntimeStateByComponent[component] = state;
            }

            return state;
        }

        private float ResolveSeedFill01(MachineComponent component)
        {
            if (component == null || component.def == null || !component.def.water)
                return 0f;

            float seeded = Mathf.Clamp01(component.WaterFill01);
            if (seeded <= 0f)
            {
                seeded = MachineComponent.NormalizeFill01(component.def.fillLevel);
            }

            return seeded;
        }

        private float ResolveComponentVolume_m3(MachineComponent component)
        {
            if (component == null || component.def == null || !component.def.water)
                return 0f;

            float volumeLiters = Mathf.Max(0f, component.def.volume); // ThingDef volume is authored in liters.
            return volumeLiters * LitersToCubicMeters;
        }

        private void CommitComponentRuntimeState(MachineComponent component, ComponentRuntimeState state)
        {
            if (component == null) return;

            componentRuntimeStateByComponent[component] = state;
            componentRuntimeStateById[state.ComponentId] = state;
            componentRuntimeStateByCell[state.AnchorCell] = state;
            component.SetWaterFillPercent(state.FillPercent);
        }

        private void BuildFlowArrows()
        {
            arrowBuffer.Clear();
            if (grid == null) return;

            foreach (var binding in pipeBindings)
            {
                if (binding.EdgeIndex < 0 || binding.EdgeIndex >= edges.Count) continue;

                var edge = edges[binding.EdgeIndex];
                float flow = (float)edge.Flow_m3s;
                if (Math.Abs(flow) <= 1e-6f) continue;

                bool forward = flow >= 0f;
                var path = forward ? binding.Path : binding.ReversePath;
                if (path == null || path.Length < 2) continue;

                float magnitude = Math.Abs(flow);
                float speed = binding.Pipe != null && binding.Pipe.flowrateMax > 0f
                    ? Mathf.Clamp01(magnitude / binding.Pipe.flowrateMax)
                    : 0f;

                var arrow = new SimulationManager.WaterFlowArrow
                {
                    StartCell = forward ? binding.StartCell : binding.EndCell,
                    EndCell = forward ? binding.EndCell : binding.StartCell,
                    Path = path,
                    PathLength = binding.PathLength,
                    TravelDistance = binding.PathLength,
                    Speed = speed,
                    Scale = 1f
                };

                arrowBuffer.Add(arrow);
            }
        }

        private void EnsureFields()
        {
            int count = grid.CellCount;
            if (pressureField == null || pressureField.Length != count)
            {
                pressureField = new float[count];
            }

            if (flowField == null || flowField.Length != count)
            {
                flowField = new float[count];
            }
        }

        private double CalculatePipeLength(PlacedPipe pipe)
        {
            if (grid == null || pipe == null || pipe.occupiedCells == null || pipe.occupiedCells.Count < 2)
            {
                return 1d;
            }

            double length = 0d;
            for (int i = 1; i < pipe.occupiedCells.Count; i++)
            {
                var a = grid.CellToWorld(pipe.occupiedCells[i - 1]);
                var b = grid.CellToWorld(pipe.occupiedCells[i]);
                length += Vector3.Distance(a, b);
            }

            return Math.Max(1e-3d, length);
        }

        private double CalculatePipeVolume(PlacedPipe pipe)
        {
            double length = CalculatePipeLength(pipe);
            double diameter = ResolvePipeDiameter(pipe);
            double area = Math.PI * 0.25d * diameter * diameter;
            double volume = area * length;
            return Math.Max(0d, volume);
        }

        private double ResolvePipeDiameter(PlacedPipe pipe)
        {
            double diameter = DefaultDiameter_m;
            if (pipe != null && pipe.pipeDef != null)
            {
                diameter = Math.Max(DefaultDiameter_m, pipe.pipeDef.innerDiameter_m);
            }

            return diameter;
        }

        private double[] ConvertMinorLosses(float[] minorLosses)
        {
            var buffer = new double[minorLosses.Length];
            for (int i = 0; i < minorLosses.Length; i++)
            {
                buffer[i] = Math.Max(0d, minorLosses[i]);
            }

            return buffer;
        }

        private List<PlacedPipe> CollectPipes()
        {
            var pipes = new List<PlacedPipe>();
            var seen = new HashSet<PlacedPipe>();

            if (grid == null) return pipes;

            for (int y = 0; y < grid.height; y++)
            {
                for (int x = 0; x < grid.width; x++)
                {
                    var cell = grid.GetCell(new Vector2Int(x, y));
                    if (!cell.HasPipe || cell.pipes == null) continue;

                    for (int i = 0; i < cell.pipes.Count; i++)
                    {
                        var pipe = cell.pipes[i];
                        if (pipe == null || !seen.Add(pipe)) continue;
                        pipes.Add(pipe);
                    }
                }
            }

            return pipes;
        }

        private void ResetPipeRuntimeSamples()
        {
            var keys = new List<PlacedPipe>(pipeRuntimeStateByPipe.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                var key = keys[i];
                var state = pipeRuntimeStateByPipe[key];
                state.Flow_m3s = 0f;
                state.InletPressure_Pa = 0f;
                state.OutletPressure_Pa = 0f;
                pipeRuntimeStateByPipe[key] = state;
            }
        }

        private void PrunePipeRuntimeState(IEnumerable<PlacedPipe> activePipes)
        {
            if (activePipes == null) return;

            var active = new HashSet<PlacedPipe>(activePipes);
            var toRemove = new List<PlacedPipe>();

            foreach (var kvp in pipeRuntimeStateByPipe)
            {
                if (kvp.Key == null || !active.Contains(kvp.Key))
                {
                    toRemove.Add(kvp.Key);
                }
            }

            for (int i = 0; i < toRemove.Count; i++)
            {
                pipeRuntimeStateByPipe.Remove(toRemove[i]);
            }
        }

        private void PruneComponentRuntimeStates()
        {
            componentRemovalBuffer.Clear();

            foreach (var kvp in componentRuntimeStateByComponent)
            {
                var component = kvp.Key;
                if (component == null || !activeWaterComponents.Contains(component) || !IsComponentAnchored(component))
                {
                    componentRemovalBuffer.Add(component);
                }
            }

            for (int i = 0; i < componentRemovalBuffer.Count; i++)
            {
                var component = componentRemovalBuffer[i];
                if (component == null)
                {
                    componentRuntimeStateByComponent.Remove(component);
                    continue;
                }

                componentRuntimeStateByComponent.Remove(component);
                componentRuntimeStateById.Remove(component.GetInstanceID());
                componentRuntimeStateByCell.Remove(component.anchorCell);
            }

            componentRemovalBuffer.Clear();
        }

        private static bool IsHydraulicSupply(MachineComponent component)
        {
            return component != null
                   && component.def != null
                   && component.def.componentType == ComponentType.ChassisWaterConnection;
        }

        private bool IsComponentAnchored(MachineComponent component)
        {
            if (grid == null || component == null) return false;

            var anchor = component.anchorCell;
            if (!grid.InBounds(anchor.x, anchor.y)) return false;

            return grid.TryGetCell(anchor, out var cell) && cell.component == component;
        }

        private static double ResolveSourcePressure(MachineComponent component)
        {
            if (component == null || component.def == null || !IsHydraulicSupply(component))
            {
                return 0d;
            }

            return Math.Max(0d, component.def.maxPressure);
        }

        private int GetOrCreateNode(Vector2Int cell, double pressure, bool isFixed, bool isSource)
        {
            if (nodeIndexByCell.TryGetValue(cell, out var existing))
            {
                var existingNode = nodes[existing];
                existingNode.IsSource |= isSource;
                existingNode.IsFixedPressure |= isFixed || isSource;
                if (isFixed || isSource)
                {
                    double clamped = Math.Max(0d, pressure);
                    existingNode.FixedPressure_Pa = Math.Max(existingNode.FixedPressure_Pa, clamped);
                    if (isSource)
                    {
                        existingNode.SourcePressure_Pa = Math.Max(existingNode.SourcePressure_Pa, clamped);
                    }
                }

                existingNode.Pressure_Pa = Math.Max(existingNode.Pressure_Pa, pressure);
                nodes[existing] = existingNode;
                return existing;
            }

            int index = nodes.Count;
            var node = new HydraulicNode
            {
                Id = index,
                Cell = cell,
                IsSource = isSource,
                IsFixedPressure = isFixed || isSource,
                FixedPressure_Pa = (isFixed || isSource) ? Math.Max(0d, pressure) : 0d,
                SourcePressure_Pa = isSource ? Math.Max(0d, pressure) : 0d,
                Pressure_Pa = Math.Max(0d, pressure),
                Injection_m3s = 0d
            };

            nodes.Add(node);
            nodeIndexByCell[cell] = index;
            return index;
        }

        private struct FlowAccumulator
        {
            private float net;
            private float dominantContribution;

            public float Net => net;

            public void Add(float contribution)
            {
                net += contribution;

                if (Mathf.Abs(contribution) > Mathf.Abs(dominantContribution))
                {
                    dominantContribution = contribution;
                }
            }

            public float ResolveDisplayFlow()
            {
                float dominantMagnitude = Mathf.Abs(dominantContribution);
                float netMagnitude = Mathf.Abs(net);

                if (netMagnitude > dominantMagnitude)
                {
                    return net;
                }

                return dominantContribution;
            }
        }

        private readonly struct PortReference
        {
            public readonly MachineComponent Component;
            public readonly int PortIndex;

            public PortReference(MachineComponent component, int portIndex)
            {
                Component = component;
                PortIndex = portIndex;
            }
        }
    }
}
