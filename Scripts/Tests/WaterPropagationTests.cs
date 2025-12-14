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
            float portFlow)
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
            thingDef.maxPressure = 10f;
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

        private PlacedPipe CreatePipe(
            MachineComponent start,
            MachineComponent end,
            float flowrate,
            List<Vector2Int> occupiedCells)
        {
            var go = new GameObject("Pipe");
            createdObjects.Add(go);

            var pipe = go.AddComponent<PlacedPipe>();
            pipe.pipeDef = ScriptableObject.CreateInstance<PipeDef>();
            pipe.pipeDef.maxFlow = flowrate;
            createdObjects.Add(pipe.pipeDef);
            pipe.startComponent = start;
            pipe.endComponent = end;
            pipe.startPortCell = start.anchorCell;
            pipe.endPortCell = end.anchorCell;
            pipe.occupiedCells = occupiedCells;
            pipe.flowrateMax = flowrate;
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
