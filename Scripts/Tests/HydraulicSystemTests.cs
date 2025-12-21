using System.Collections.Generic;
using System.Reflection;
using MachineRepair;
using MachineRepair.Fluid;
using MachineRepair.Grid;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MachineRepair.Tests
{
    public class HydraulicSystemTests
    {
        private readonly List<Object> createdObjects = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < createdObjects.Count; i++)
            {
                if (createdObjects[i] != null)
                {
                    Object.DestroyImmediate(createdObjects[i]);
                }
            }

            createdObjects.Clear();
        }

        [Test]
        public void HydraulicSystem_ComputesFlowAndSnapshotsForWaterPorts()
        {
            var grid = CreateTestGrid();

            var source = CreateWaterComponent(grid, new Vector2Int(0, 0), maxPressure: 200_000f);
            var sink = CreateWaterComponent(grid, new Vector2Int(2, 0), maxPressure: 0f, componentType: ComponentType.Boiler);

            _ = CreatePipeRun(grid, source, sink, new List<Vector2Int>
            {
                source.component.GetGlobalCell(source.port),
                new Vector2Int(1, 0),
                sink.component.GetGlobalCell(sink.port)
            });

            var system = new HydraulicSystem(grid);
            system.SetWaterEnabled(true);
            system.Solve();

            var sourceCell = source.component.GetGlobalCell(source.port);
            var sinkCell = sink.component.GetGlobalCell(sink.port);

            Assert.That(system.PortStates.ContainsKey(sourceCell), "Source port should report hydraulic state.");
            Assert.That(system.PortStates.ContainsKey(sinkCell), "Sink port should report hydraulic state.");

            var sourceState = system.PortStates[sourceCell];
            var sinkState = system.PortStates[sinkCell];

            Assert.That(Mathf.Abs(sourceState.Flow_m3s), Is.GreaterThan(1e-6f), "Source flow should be non-zero.");
            Assert.That(Mathf.Abs(sinkState.Flow_m3s), Is.GreaterThan(1e-6f), "Sink flow should be non-zero.");
            Assert.That(sourceState.Pressure_Pa, Is.GreaterThan(sinkState.Pressure_Pa), "Pressure should drop across the pipe run.");
            Assert.IsTrue(sourceState.IsSource, "Chassis connections should be flagged as sources.");
            Assert.IsFalse(sinkState.IsSource, "Non-chassis connections should not be sources.");
            Assert.That(sinkState.Pressure_Pa, Is.GreaterThan(0f), "Sink nodes should receive propagated pressure.");
            Assert.That(system.SourceByCell[sourceCell], Is.True);
            Assert.That(system.SourceByCell[sinkCell], Is.False);

            Vector2Int pipeCell = new Vector2Int(1, 0);
            Assert.That(system.PipeDeltaPByCell.ContainsKey(pipeCell), "Pipe ΔP should be recorded for occupied cells.");
            Assert.That(system.PipeDeltaPByCell[pipeCell], Is.GreaterThan(0f));

            var simulationManagerObject = new GameObject("SimulationManager");
            createdObjects.Add(simulationManagerObject);
            var simulationManager = simulationManagerObject.AddComponent<SimulationManager>();
            SetPrivateField(simulationManager, "grid", grid);

            simulationManager.RunSimulationStep();
            Assert.That(simulationManager.LastSnapshot.HasValue, "Simulation snapshot should be produced after a run.");

            var snapshot = simulationManager.LastSnapshot.Value.PortHydraulic;
            Assert.That(snapshot.ContainsKey(sourceCell), "Snapshot should contain the source port.");
            Assert.That(snapshot.ContainsKey(sinkCell), "Snapshot should contain the sink port.");
            Assert.That(Mathf.Abs(snapshot[sourceCell].Flow_m3s), Is.GreaterThan(1e-6f));
            Assert.That(Mathf.Abs(snapshot[sinkCell].Flow_m3s), Is.GreaterThan(1e-6f));
            Assert.That(snapshot[sourceCell].IsSource, Is.True, "Snapshot should preserve source flags.");
            Assert.That(snapshot[sinkCell].IsSource, Is.False);

            var snapshotValue = simulationManager.LastSnapshot.Value;
            Assert.That(snapshotValue.HydraulicSources.ContainsKey(sourceCell) && snapshotValue.HydraulicSources[sourceCell]);
            Assert.That(snapshotValue.HydraulicSources.ContainsKey(sinkCell) && !snapshotValue.HydraulicSources[sinkCell]);
            Assert.That(snapshotValue.TryGetPipeDeltaP(pipeCell, out var snapshotDeltaP) && snapshotDeltaP > 0f, "Snapshot should surface pipe ΔP values.");
        }

        [Test]
        public void HydraulicSystem_NonSourceNodesStartAtNeutralPressure()
        {
            var grid = CreateTestGrid();
            var isolated = CreateWaterComponent(grid, new Vector2Int(1, 0), maxPressure: 150_000f, componentType: ComponentType.Boiler);

            var system = new HydraulicSystem(grid);
            system.SetWaterEnabled(true);
            system.Solve();

            Vector2Int portCell = isolated.component.GetGlobalCell(isolated.port);
            Assert.That(system.PortStates.ContainsKey(portCell), "Isolated port should produce a hydraulic state entry.");

            var state = system.PortStates[portCell];
            Assert.IsFalse(state.IsSource, "Non-supply nodes should not be flagged as sources.");
            Assert.That(state.SourcePressure_Pa, Is.EqualTo(0f), "Non-supply nodes should not expose a source pressure.");
            Assert.That(state.Pressure_Pa, Is.EqualTo(0f).Within(1e-5f), "Non-supply nodes should start at neutral pressure.");
        }

        [Test]
        public void HydraulicSystem_DisablesWaterAndClearsOutput()
        {
            var grid = CreateTestGrid();
            _ = CreateWaterComponent(grid, new Vector2Int(0, 0), maxPressure: 100_000f);

            var system = new HydraulicSystem(grid);
            system.SetWaterEnabled(true);
            system.Solve();

            system.SetWaterEnabled(false);
            system.Solve();

            Assert.That(system.PortStates, Is.Empty, "Port states should be cleared when water is disabled.");
            Assert.That(system.PressureField, Has.Length.EqualTo(grid.CellCount));
            Assert.That(system.FlowField, Has.Length.EqualTo(grid.CellCount));
            Assert.That(system.PressureField, Is.All.EqualTo(0f), "Pressure field should be zeroed when disabled.");
            Assert.That(system.FlowField, Is.All.EqualTo(0f), "Flow field should be zeroed when disabled.");
        }

        private GridManager CreateTestGrid()
        {
            var go = new GameObject("GridManager");
            createdObjects.Add(go);

            var grid = go.AddComponent<GridManager>();
            grid.width = 3;
            grid.height = 1;

            int cellCount = grid.CellCount;
            var terrain = new CellTerrain[cellCount];
            var occupancy = new CellOccupancy[cellCount];
            var basePlaceability = new CellPlaceability[cellCount];
            var buckets = new List<ThingDef>[cellCount];
            var spill = new float[cellCount];
            var power = new bool[cellCount];
            var water = new bool[cellCount];

            for (int i = 0; i < cellCount; i++)
            {
                terrain[i] = new CellTerrain
                {
                    index = i,
                    placeability = CellPlaceability.Placeable,
                    isDisplayZone = false
                };
                occupancy[i] = new CellOccupancy();
                basePlaceability[i] = CellPlaceability.Placeable;
                buckets[i] = new List<ThingDef>();
            }

            SetPrivateField(grid, "terrainByIndex", terrain);
            SetPrivateField(grid, "occupancyByIndex", occupancy);
            SetPrivateField(grid, "basePlaceabilityByIndex", basePlaceability);
            SetPrivateField(grid, "bucketByIndex", buckets);
            SetPrivateField(grid, "spillByIndex", spill);
            SetPrivateField(grid, "powerByIndex", power);
            SetPrivateField(grid, "waterByIndex", water);

            return grid;
        }

        private (MachineComponent component, PortLocal port) CreateWaterComponent(
            GridManager grid,
            Vector2Int anchor,
            float maxPressure,
            ComponentType componentType = ComponentType.ChassisWaterConnection)
        {
            var go = new GameObject($"WaterComponent_{anchor}");
            createdObjects.Add(go);

            var component = go.AddComponent<MachineComponent>();
            component.grid = grid;
            component.anchorCell = anchor;
            component.rotation = 0;

            var portDef = ScriptableObject.CreateInstance<PortDef>();
            createdObjects.Add(portDef);
            portDef.ports = new[]
            {
                new PortLocal
                {
                    cell = Vector2Int.zero,
                    portType = PortType.Water,
                    internalConnectionIndices = new int[0],
                    flowrateMax = 0.01f
                }
            };

            var footprint = new FootprintMask
            {
                width = 1,
                height = 1,
                origin = Vector2Int.zero,
                occupied = new[] { true },
                display = new[] { false },
                connectedPorts = portDef
            };

            var def = ScriptableObject.CreateInstance<ThingDef>();
            createdObjects.Add(def);
            def.componentType = componentType;
            def.water = true;
            def.maxPressure = maxPressure;
            def.footprintMask = footprint;

            component.def = def;
            component.footprint = footprint;
            component.portDef = portDef;

            var occupancy = GetOccupancy(grid);
            var index = grid.ToIndex(anchor);
            var cell = occupancy[index];
            cell.component = component;
            occupancy[index] = cell;

            return (component, portDef.ports[0]);
        }

        private PlacedPipe CreatePipeRun(
            GridManager grid,
            (MachineComponent component, PortLocal port) start,
            (MachineComponent component, PortLocal port) end,
            List<Vector2Int> occupiedCells)
        {
            var go = new GameObject("Pipe");
            createdObjects.Add(go);

            var pipe = go.AddComponent<PlacedPipe>();
            pipe.startComponent = start.component;
            pipe.endComponent = end.component;
            pipe.startPortCell = start.component.GetGlobalCell(start.port);
            pipe.endPortCell = end.component.GetGlobalCell(end.port);
            pipe.occupiedCells = occupiedCells;
            pipe.flowrateMax = 1f;

            var occupancy = GetOccupancy(grid);
            for (int i = 0; i < occupiedCells.Count; i++)
            {
                var idx = grid.ToIndex(occupiedCells[i]);
                var cell = occupancy[idx];
                cell.AddPipe(pipe);
                occupancy[idx] = cell;
            }

            return pipe;
        }

        private static CellOccupancy[] GetOccupancy(GridManager grid)
        {
            var field = typeof(GridManager).GetField("occupancyByIndex", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field, "GridManager should expose occupancy for tests.");
            return (CellOccupancy[])field.GetValue(grid);
        }

        private static void SetPrivateField<T>(object target, string name, T value)
        {
            var field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field, $"{target.GetType().Name} should have a field named {name}.");
            field.SetValue(target, value);
        }
    }
}
