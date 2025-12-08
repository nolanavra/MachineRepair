
using System.Collections.Generic;
using MachineRepair;
using MachineRepair.Grid;
using UnityEngine;

namespace MachineRepair.Flavor
{
    public class DefaultFlavorContextSource : MonoBehaviour, IFlavorContextSource
    {
        [Header("Data Sources")]
        public GridManager grid;
        public WirePlacementTool wirePlacement;
        public PipePlacementTool pipePlacement;
        public InputRouter inputRouter;
        public double sessionStartTime;

        void Awake()
        {
            if (sessionStartTime <= 0) sessionStartTime = Time.realtimeSinceStartupAsDouble;
        }

        public FlavorContext CaptureFlavorContext()
        {
            var ctx = new FlavorContext();
            ctx.sessionSeconds = Time.realtimeSinceStartupAsDouble - sessionStartTime;

            int total = 0, boilers=0, valves=0, pumps=0, groups=0, pblocks=0;

            bool hasWaterRoute = false;
            bool hasPowerRoute = false;

            if (grid != null && grid.setup)
            {
                HashSet<MachineComponent> counted = new HashSet<MachineComponent>();
                for (int x = 0; x < grid.width; x++)
                for (int y = 0; y < grid.height; y++)
                {
                    var cellPos = new Vector2Int(x, y);
                    hasWaterRoute |= grid.HasWater(cellPos);
                    hasPowerRoute |= grid.HasPower(cellPos);

                    var cell = grid.GetCell(cellPos);
                    var component = cell.component;
                    if (component == null) continue;
                    if (!counted.Add(component)) continue;

                    total++;

                    var def = component.def;
                    ComponentType type = def != null ? def.type : ComponentType.None;
                    switch (type)
                    {
                        case ComponentType.Boiler:
                            boilers++;
                            break;
                        case ComponentType.SolonoidValve:
                            valves++;
                            break;
                        case ComponentType.Pump:
                            pumps++;
                            break;
                        case ComponentType.Grouphead:
                            groups++;
                            break;
                        case ComponentType.ChassisPowerConnection:
                            pblocks++;
                            break;
                        default:
                            string name = def != null ? (def.displayName ?? def.defName ?? def.name) : component.name;
                            string lower = string.IsNullOrEmpty(name) ? string.Empty : name.ToLowerInvariant();
                            if (lower.Contains("boiler")) boilers++;
                            else if (lower.Contains("valve")) valves++;
                            else if (lower.Contains("pump")) pumps++;
                            else if (lower.Contains("group")) groups++;
                            else if (lower.Contains("power")) pblocks++;
                            break;
                    }
                }
            }

            ctx.hasWaterRoute = hasWaterRoute;
            ctx.hasPowerRoute = hasPowerRoute;

            ctx.totalParts = total;
            ctx.boilers = boilers;
            ctx.valves = valves;
            ctx.pumps = pumps;
            ctx.groupheads = groups;
            ctx.powerBlocks = pblocks;

            var modeManager = GameModeManager.Instance;
            bool placingMode = false;

            if (modeManager != null)
            {
                placingMode = modeManager.CurrentMode == GameMode.ComponentPlacement ||
                              modeManager.CurrentMode == GameMode.WirePlacement ||
                              modeManager.CurrentMode == GameMode.PipePlacement;
            }
            else
            {
                placingMode = (wirePlacement != null && wirePlacement.isActiveAndEnabled) ||
                              (pipePlacement != null && pipePlacement.isActiveAndEnabled) ||
                              (inputRouter != null && inputRouter.isActiveAndEnabled);
            }

            ctx.inPlaceMode = placingMode;
            ctx.inInspectMode = !placingMode;

            return ctx;
        }
    }
}
