using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MachineRepair.Grid;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

namespace MachineRepair.Tests
{
    public class WaterPropagationTests
    {
        private readonly List<Object> createdObjects = new();

        private class SilentGridManager : GridManager
        {
            private void Awake()
            {
                // Prevent automatic InitGrids; tests configure explicitly.
            }
        }

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
        public void PipeFillProgressesBeforeComponentConnections()
        {
            var grid = CreateGrid(3, 1);
            var sim = CreateSimulationManager(grid);

            var source = CreateWaterComponent(grid, "Source", ComponentType.ChassisWaterConnection, new Vector2Int(0, 0), 50f);
            var sink = CreateWaterComponent(grid, "Sink", ComponentType.Boiler, new Vector2Int(2, 0), 25f);

            Assert.IsTrue(grid.TryPlaceComponent(source.anchorCell, source));
            Assert.IsTrue(grid.TryPlaceComponent(sink.anchorCell, sink));

            var pipe = CreatePipe(source, sink, 50f, new List<Vector2Int>
            {
                new Vector2Int(0, 0),
                new Vector2Int(1, 0),
                new Vector2Int(2, 0)
            });

            Assert.IsTrue(grid.AddPipeRun(pipe.occupiedCells, pipe));

            RunWaterStep(sim);

            Assert.That(pipe.fillLevel, Is.InRange(49.9f, 50.1f), "Pipe should partially fill on first step.");
            Assert.AreEqual(0f, GetComponentFill(sim, sink), "Downstream component should not receive flow until pipe is full.");

            RunWaterStep(sim);

            Assert.That(pipe.fillLevel, Is.EqualTo(100f).Within(0.01f), "Pipe should reach full after second step.");
            Assert.Greater(GetComponentFill(sim, sink), 0f, "Filled pipe should forward flow to connected component ports.");
        }

        [Test]
        public void WaterArrowsCoverFullRunLength()
        {
            var grid = CreateGrid(3, 1);
            var sim = CreateSimulationManager(grid);

            var source = CreateWaterComponent(grid, "Source", ComponentType.ChassisWaterConnection, new Vector2Int(0, 0), 50f);
            var sink = CreateWaterComponent(grid, "Sink", ComponentType.Boiler, new Vector2Int(2, 0), 25f);

            Assert.IsTrue(grid.TryPlaceComponent(source.anchorCell, source));
            Assert.IsTrue(grid.TryPlaceComponent(sink.anchorCell, sink));

            var occupied = new List<Vector2Int>
            {
                new Vector2Int(0, 0),
                new Vector2Int(1, 0),
                new Vector2Int(2, 0)
            };

            var pipe = CreatePipe(source, sink, 50f, occupied);

            Assert.IsTrue(grid.AddPipeRun(pipe.occupiedCells, pipe));

            var capturedArrows = new List<SimulationManager.WaterFlowArrow>();
            sim.WaterFlowUpdated += arrows =>
            {
                capturedArrows = arrows == null
                    ? new List<SimulationManager.WaterFlowArrow>()
                    : new List<SimulationManager.WaterFlowArrow>(arrows);
            };

            RunWaterStep(sim);

            Assert.AreEqual(1, capturedArrows.Count, "A continuous pipe should emit exactly one arrow.");

            float expectedLength = CalculatePathLength(grid, pipe.occupiedCells);
            Assert.That(capturedArrows[0].PathLength, Is.EqualTo(expectedLength).Within(0.001f),
                "Arrow path length should span the full pipe run.");
            Assert.That(capturedArrows[0].TravelDistance, Is.EqualTo(expectedLength * 0.5f).Within(0.001f),
                "Partially filled pipe should produce half-length arrow travel distance.");

            RunWaterStep(sim);

            Assert.AreEqual(1, capturedArrows.Count, "Arrow spawning should remain one per run.");
            Assert.That(capturedArrows[0].TravelDistance, Is.EqualTo(expectedLength).Within(0.001f),
                "Full pipe should produce travel distance equal to run length.");
        }

        [Test]
        public void WaterArrowPathIncludesTurns()
        {
            var grid = CreateGrid(3, 2);
            var sim = CreateSimulationManager(grid);

            var source = CreateWaterComponent(grid, "Source", ComponentType.ChassisWaterConnection, new Vector2Int(0, 0), 50f);
            var sink = CreateWaterComponent(grid, "Sink", ComponentType.Boiler, new Vector2Int(2, 1), 25f);

            Assert.IsTrue(grid.TryPlaceComponent(source.anchorCell, source));
            Assert.IsTrue(grid.TryPlaceComponent(sink.anchorCell, sink));

            var occupied = new List<Vector2Int>
            {
                new Vector2Int(0, 0),
                new Vector2Int(1, 0),
                new Vector2Int(1, 1),
                new Vector2Int(2, 1)
            };

            var pipe = CreatePipe(source, sink, 50f, occupied);
            Assert.IsTrue(grid.AddPipeRun(pipe.occupiedCells, pipe));

            var capturedArrows = new List<SimulationManager.WaterFlowArrow>();
            sim.WaterFlowUpdated += arrows =>
            {
                capturedArrows = arrows == null
                    ? new List<SimulationManager.WaterFlowArrow>()
                    : new List<SimulationManager.WaterFlowArrow>(arrows);
            };

            RunWaterStep(sim);

            Assert.AreEqual(1, capturedArrows.Count, "Turned pipe should still render a single arrow.");

            var arrow = capturedArrows[0];
            Assert.AreEqual(pipe.occupiedCells.Count, arrow.Path.Length, "Arrow path should include every cell in the run.");

            for (int i = 0; i < pipe.occupiedCells.Count; i++)
            {
                Vector3 expected = grid.CellToWorld(pipe.occupiedCells[i]);
                Assert.That(arrow.Path[i], Is.EqualTo(expected).Within(0.0001f),
                    $"Arrow path point {i} should align with pipe cell {pipe.occupiedCells[i]}.");
            }
        }

        [Test]
        public void PressureDropsAcrossPipeLength()
        {
            var grid = CreateGrid(4, 1);
            var sim = CreateSimulationManager(grid);
            SetPressureDropPerCell(sim, 0.5f);

            const float maxPressure = 12f;
            var source = CreateWaterComponent(grid, "Source", ComponentType.ChassisWaterConnection, new Vector2Int(0, 0), 100f, maxPressure);
            var sink = CreateWaterComponent(grid, "Sink", ComponentType.Boiler, new Vector2Int(3, 0), 100f, maxPressure);

            Assert.IsTrue(grid.TryPlaceComponent(source.anchorCell, source));
            Assert.IsTrue(grid.TryPlaceComponent(sink.anchorCell, sink));

            var occupied = new List<Vector2Int>
            {
                new Vector2Int(0, 0),
                new Vector2Int(1, 0),
                new Vector2Int(2, 0),
                new Vector2Int(3, 0)
            };

            var pipe = CreatePipe(source, sink, 100f, occupied, maxPressure);
            Assert.IsTrue(grid.AddPipeRun(pipe.occupiedCells, pipe));

            RunWaterStep(sim);

            var pressureGraph = GetPressureGraph(sim);
            Assert.IsNotNull(pressureGraph);

            for (int i = 0; i < pipe.occupiedCells.Count; i++)
            {
                var cell = pipe.occupiedCells[i];
                int idx = grid.ToIndex(cell);
                int stepsToCell = CalculatePathSteps(pipe.occupiedCells, i);
                float expectedPressure = Mathf.Max(0f, maxPressure - 0.5f * stepsToCell);

                Assert.That(pressureGraph[idx], Is.EqualTo(expectedPressure).Within(0.001f),
                    $"Pressure should drop per cell traveled. Cell {cell} expected {expectedPressure:0.###} after {stepsToCell} steps.");
            }
        }

        [Test]
        public void UnconnectedWaterPortGeneratesLeak()
        {
            var grid = CreateGrid(1, 1);
            var sim = CreateSimulationManager(grid);

            var source = CreateWaterComponent(grid, "Source", ComponentType.ChassisWaterConnection, Vector2Int.zero, 10f);
            Assert.IsTrue(grid.TryPlaceComponent(source.anchorCell, source));

            var leakCells = new List<Vector2Int>();
            sim.LeaksUpdated += leaks => leakCells.AddRange(leaks.Select(l => l.Cell));

            RunWaterStep(sim);

            CollectionAssert.Contains(leakCells, Vector2Int.zero, "Open water port should register as a leak when reached.");
        }

        [Test]
        public void FlowPropagatesFromHigherPressurePortOnly()
        {
            var grid = CreateGrid(2, 1);
            var sim = CreateSimulationManager(grid);

            var high = CreateWaterComponent(grid, "High", ComponentType.ChassisWaterConnection, new Vector2Int(0, 0), 100f, 12f);
            var low = CreateWaterComponent(grid, "Low", ComponentType.ChassisWaterConnection, new Vector2Int(1, 0), 100f, 6f);

            Assert.IsTrue(grid.TryPlaceComponent(high.anchorCell, high));
            Assert.IsTrue(grid.TryPlaceComponent(low.anchorCell, low));

            var pipe = CreatePipe(high, low, 100f, new List<Vector2Int>
            {
                new Vector2Int(0, 0),
                new Vector2Int(1, 0)
            }, 12f);

            Assert.IsTrue(grid.AddPipeRun(pipe.occupiedCells, pipe));

            var capturedArrows = new List<SimulationManager.WaterFlowArrow>();
            sim.WaterFlowUpdated += arrows =>
            {
                capturedArrows = arrows == null
                    ? new List<SimulationManager.WaterFlowArrow>()
                    : new List<SimulationManager.WaterFlowArrow>(arrows);
            };

            RunWaterStep(sim);
            RunWaterStep(sim);

            Assert.AreEqual(1, capturedArrows.Count, "Only one directed water arrow should be present between two connected ports.");
            Assert.AreEqual(high.anchorCell, capturedArrows[0].StartCell, "Arrow should originate from the higher-pressure port.");
            Assert.AreEqual(low.anchorCell, capturedArrows[0].EndCell, "Arrow should point toward the lower-pressure port.");

            var pressureGraph = GetPressureGraph(sim);
            Assert.IsNotNull(pressureGraph);

            int highIndex = grid.ToIndex(high.anchorCell);
            int lowIndex = grid.ToIndex(low.anchorCell);

            Assert.Greater(pressureGraph[highIndex], pressureGraph[lowIndex] + 0.0001f,
                "Higher-pressure port should retain a greater recorded pressure than the lower-pressure port.");
        }

        [Test]
        public void PumpBoostsPressureWhenPowered()
        {
            var grid = CreateGrid(5, 1);
            var sim = CreateSimulationManager(grid);

            var source = CreateWaterComponent(grid, "Source", ComponentType.ChassisWaterConnection, new Vector2Int(0, 0), 100f, 6f);
            var pump = CreatePumpComponent(grid, "Pump", new Vector2Int(1, 0), 100f, 6f, 12f, requiresPower: true);
            var sink = CreateWaterComponent(grid, "Sink", ComponentType.Boiler, new Vector2Int(4, 0), 100f, 6f);

            Assert.IsTrue(grid.TryPlaceComponent(source.anchorCell, source));
            Assert.IsTrue(grid.TryPlaceComponent(pump.anchorCell, pump));
            Assert.IsTrue(grid.TryPlaceComponent(sink.anchorCell, sink));

            var inletPipe = CreatePipe(source, pump, 100f, new List<Vector2Int>
            {
                new Vector2Int(0, 0),
                new Vector2Int(1, 0)
            }, 12f, endPortCellOverride: pump.anchorCell);
            var outletPipe = CreatePipe(pump, sink, 100f, new List<Vector2Int>
            {
                new Vector2Int(2, 0),
                new Vector2Int(3, 0),
                new Vector2Int(4, 0)
            }, 12f, startPortCellOverride: new Vector2Int(2, 0), endPortCellOverride: sink.anchorCell);

            Assert.IsTrue(grid.AddPipeRun(inletPipe.occupiedCells, inletPipe));
            Assert.IsTrue(grid.AddPipeRun(outletPipe.occupiedCells, outletPipe));

            var poweredComponents = typeof(SimulationManager)
                .GetField("poweredComponents", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(sim) as HashSet<MachineComponent>;
            poweredComponents?.Add(pump);
            pump.SetPowered(true);

            RunWaterStep(sim);
            RunWaterStep(sim);

            var pressureGraph = GetPressureGraph(sim);
            Assert.IsNotNull(pressureGraph);

            int sourceIndex = grid.ToIndex(source.anchorCell);
            int sinkIndex = grid.ToIndex(sink.anchorCell);

            Assert.Greater(pressureGraph[sinkIndex], pressureGraph[sourceIndex] + 0.001f,
                "Powered pump should raise downstream pressure above the upstream source.");
        }

        [Test]
        public void PumpDoesNotBoostWhenUnpowered()
        {
            var grid = CreateGrid(5, 1);
            var sim = CreateSimulationManager(grid);

            var source = CreateWaterComponent(grid, "Source", ComponentType.ChassisWaterConnection, new Vector2Int(0, 0), 100f, 6f);
            var pump = CreatePumpComponent(grid, "Pump", new Vector2Int(1, 0), 100f, 6f, 12f, requiresPower: true);
            var sink = CreateWaterComponent(grid, "Sink", ComponentType.Boiler, new Vector2Int(4, 0), 100f, 6f);

            Assert.IsTrue(grid.TryPlaceComponent(source.anchorCell, source));
            Assert.IsTrue(grid.TryPlaceComponent(pump.anchorCell, pump));
            Assert.IsTrue(grid.TryPlaceComponent(sink.anchorCell, sink));

            var inletPipe = CreatePipe(source, pump, 100f, new List<Vector2Int>
            {
                new Vector2Int(0, 0),
                new Vector2Int(1, 0)
            }, 12f, endPortCellOverride: pump.anchorCell);
            var outletPipe = CreatePipe(pump, sink, 100f, new List<Vector2Int>
            {
                new Vector2Int(2, 0),
                new Vector2Int(3, 0),
                new Vector2Int(4, 0)
            }, 12f, startPortCellOverride: new Vector2Int(2, 0), endPortCellOverride: sink.anchorCell);

            Assert.IsTrue(grid.AddPipeRun(inletPipe.occupiedCells, inletPipe));
            Assert.IsTrue(grid.AddPipeRun(outletPipe.occupiedCells, outletPipe));

            RunWaterStep(sim);
            RunWaterStep(sim);

            var pressureGraph = GetPressureGraph(sim);
            Assert.IsNotNull(pressureGraph);

            int sourceIndex = grid.ToIndex(source.anchorCell);
            int sinkIndex = grid.ToIndex(sink.anchorCell);

            Assert.LessOrEqual(pressureGraph[sinkIndex], pressureGraph[sourceIndex] + 0.001f,
                "Unpowered pump should not raise downstream pressure above the upstream source.");
        }

        private SilentGridManager CreateGrid(int width, int height)
        {
            var gridGO = new GameObject("Grid");
            createdObjects.Add(gridGO);

            var tilemapGO = new GameObject("Tilemap");
            createdObjects.Add(tilemapGO);

            tilemapGO.transform.SetParent(gridGO.transform);
            tilemapGO.AddComponent<UnityEngine.Grid>();
            var tilemap = tilemapGO.AddComponent<Tilemap>();

            var gridManager = gridGO.AddComponent<SilentGridManager>();
            gridManager.width = width;
            gridManager.height = height;

            var tilemapField = typeof(GridManager).GetField("tilemap", BindingFlags.NonPublic | BindingFlags.Instance);
            tilemapField.SetValue(gridManager, tilemap);

            var initPlaceability = typeof(GridManager).GetMethod("cellDefByType", BindingFlags.NonPublic | BindingFlags.Instance);
            initPlaceability.Invoke(gridManager, null);

            gridManager.InitGrids();
            ForcePlaceableTerrain(gridManager);
            return gridManager;
        }

        private SimulationManager CreateSimulationManager(GridManager grid)
        {
            var simGO = new GameObject("Simulation");
            createdObjects.Add(simGO);

            var sim = simGO.AddComponent<SimulationManager>();
            var gridField = typeof(SimulationManager).GetField("grid", BindingFlags.NonPublic | BindingFlags.Instance);
            gridField.SetValue(sim, grid);

            return sim;
        }

        private MachineComponent CreateWaterComponent(
            GridManager grid,
            string name,
            ComponentType type,
            Vector2Int anchor,
            float portFlow,
            float maxPressure = 10f)
        {
            var go = new GameObject(name);
            createdObjects.Add(go);

            var component = go.AddComponent<MachineComponent>();
            var portDef = ScriptableObject.CreateInstance<PortDef>();
            portDef.ports = new[]
            {
                new PortLocal
                {
                    cell = Vector2Int.zero,
                    portType = PortType.Water,
                    internalConnectionIndices = new int[0],
                    flowrateMax = portFlow
                }
            };
            createdObjects.Add(portDef);

            var thingDef = ScriptableObject.CreateInstance<ThingDef>();
            thingDef.displayName = name;
            thingDef.componentType = type;
            thingDef.water = true;
            thingDef.maxPressure = maxPressure;
            thingDef.footprintMask = new FootprintMask
            {
                width = 1,
                height = 1,
                origin = Vector2Int.zero,
                occupied = new[] { true },
                display = new[] { false },
                connectedPorts = portDef
            };
            createdObjects.Add(thingDef);

            component.def = thingDef;
            component.grid = grid;
            component.anchorCell = anchor;
            component.portDef = portDef;
            component.footprint = thingDef.footprintMask;

            return component;
        }

        private MachineComponent CreatePumpComponent(
            GridManager grid,
            string name,
            Vector2Int anchor,
            float portFlow,
            float inputMaxPressure,
            float outputPressure,
            bool requiresPower)
        {
            var go = new GameObject(name);
            createdObjects.Add(go);

            var machineComponent = go.AddComponent<MachineComponent>();
            var pumpComponent = go.AddComponent<PumpComponent>();

            pumpComponent.OutputPressure = outputPressure;
            pumpComponent.RequiresPower = requiresPower;
            pumpComponent.UseAbsoluteOutput = true;
            pumpComponent.PressureBoost = 0f;

            var portDef = ScriptableObject.CreateInstance<PortDef>();
            portDef.ports = new[]
            {
                new PortLocal
                {
                    cell = Vector2Int.zero,
                    portType = PortType.Water,
                    internalConnectionIndices = new[] { 1 },
                    flowrateMax = portFlow
                },
                new PortLocal
                {
                    cell = new Vector2Int(1, 0),
                    portType = PortType.Water,
                    internalConnectionIndices = new[] { 0 },
                    flowrateMax = portFlow
                }
            };
            createdObjects.Add(portDef);

            var thingDef = ScriptableObject.CreateInstance<ThingDef>();
            thingDef.displayName = name;
            thingDef.componentType = ComponentType.Pump;
            thingDef.water = true;
            thingDef.power = requiresPower;
            thingDef.maxPressure = inputMaxPressure;
            thingDef.footprintMask = new FootprintMask
            {
                width = 2,
                height = 1,
                origin = Vector2Int.zero,
                occupied = new[] { true, true },
                display = new[] { false, false },
                connectedPorts = portDef
            };
            createdObjects.Add(thingDef);

            machineComponent.def = thingDef;
            machineComponent.grid = grid;
            machineComponent.anchorCell = anchor;
            machineComponent.portDef = portDef;
            machineComponent.footprint = thingDef.footprintMask;

            return machineComponent;
        }

        private PlacedPipe CreatePipe(
            MachineComponent start,
            MachineComponent end,
            float flowrate,
            List<Vector2Int> occupiedCells,
            float maxPressure = 10f,
            Vector2Int? startPortCellOverride = null,
            Vector2Int? endPortCellOverride = null)
        {
            var go = new GameObject("Pipe");
            createdObjects.Add(go);

            var pipe = go.AddComponent<PlacedPipe>();
            pipe.pipeDef = ScriptableObject.CreateInstance<PipeDef>();
            pipe.pipeDef.maxFlow = flowrate;
            pipe.pipeDef.maxPressure = maxPressure;
            createdObjects.Add(pipe.pipeDef);
            pipe.startComponent = start;
            pipe.endComponent = end;
            pipe.startPortCell = startPortCellOverride ?? start.anchorCell;
            pipe.endPortCell = endPortCellOverride ?? end.anchorCell;
            pipe.occupiedCells = occupiedCells;
            pipe.flowrateMax = flowrate;
            pipe.pressure = maxPressure;
            pipe.fillLevel = 0f;

            return pipe;
        }

        private float CalculatePathLength(GridManager grid, IReadOnlyList<Vector2Int> cells)
        {
            float length = 0f;
            if (grid == null || cells == null) return length;

            for (int i = 0; i < cells.Count - 1; i++)
            {
                length += Vector3.Distance(grid.CellToWorld(cells[i]), grid.CellToWorld(cells[i + 1]));
            }

            return length;
        }

        private static int CalculatePathSteps(IReadOnlyList<Vector2Int> cells, int inclusiveEndIndex)
        {
            int steps = 0;
            if (cells == null) return steps;

            for (int i = 1; i <= inclusiveEndIndex && i < cells.Count; i++)
            {
                var previous = cells[i - 1];
                var current = cells[i];
                steps += Mathf.Max(Mathf.Abs(current.x - previous.x), Mathf.Abs(current.y - previous.y));
            }

            return steps;
        }

        private float GetComponentFill(SimulationManager sim, MachineComponent component)
        {
            var fillDict = typeof(SimulationManager)
                .GetField("componentFillLevels", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(sim) as Dictionary<MachineComponent, float>;

            if (fillDict != null && fillDict.TryGetValue(component, out var fill))
            {
                return fill;
            }

            return 0f;
        }

        private float[] GetPressureGraph(SimulationManager sim)
        {
            return typeof(SimulationManager)
                .GetField("pressureGraph", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(sim) as float[];
        }

        private void SetPressureDropPerCell(SimulationManager sim, float drop)
        {
            var field = typeof(SimulationManager).GetField("pressureDropPerCell", BindingFlags.NonPublic | BindingFlags.Instance);
            field?.SetValue(sim, drop);
        }

        private void RunWaterStep(SimulationManager sim)
        {
            var buildGraphs = typeof(SimulationManager).GetMethod("BuildGraphs", BindingFlags.NonPublic | BindingFlags.Instance);
            var propagate = typeof(SimulationManager).GetMethod("PropagatePressureFlow", BindingFlags.NonPublic | BindingFlags.Instance);

            buildGraphs.Invoke(sim, null);
            propagate.Invoke(sim, null);
        }

        private void ForcePlaceableTerrain(GridManager grid)
        {
            var terrainField = typeof(GridManager).GetField("terrainByIndex", BindingFlags.NonPublic | BindingFlags.Instance);
            var baseField = typeof(GridManager).GetField("basePlaceabilityByIndex", BindingFlags.NonPublic | BindingFlags.Instance);

            var terrain = terrainField?.GetValue(grid) as MachineRepair.Grid.CellTerrain[];
            var basePlaceability = baseField?.GetValue(grid) as MachineRepair.CellPlaceability[];

            if (terrain != null)
            {
                for (int i = 0; i < terrain.Length; i++)
                {
                    terrain[i].placeability = MachineRepair.CellPlaceability.Placeable;
                }
            }

            if (basePlaceability != null)
            {
                for (int i = 0; i < basePlaceability.Length; i++)
                {
                    basePlaceability[i] = MachineRepair.CellPlaceability.Placeable;
                }
            }
        }
    }
}
