
using UnityEngine;

namespace MachineRepair.Flavor
{
    public class DefaultFlavorContextSource : MonoBehaviour, IFlavorContextSource
    {
        public GridChassis chassis;
        public PlacementSystem placement;
        public RouteLayer waterLayer;
        public RouteLayer powerLayer;
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

            if (chassis != null && chassis.occupancy != null)
            {
                for (int x = 0; x < chassis.width; x++)
                for (int y = 0; y < chassis.height; y++)
                {
                    var inst = chassis.occupancy[x, y];
                    if (inst == null) continue;
                    if (inst.anchor.x == x && inst.anchor.y == y)
                    {
                        total++;
                        var n = inst.def ? inst.def.name.ToLowerInvariant() : "";
                        if (n.Contains("boiler")) boilers++;
                        else if (n.Contains("valve")) valves++;
                        else if (n.Contains("pump")) pumps++;
                        else if (n.Contains("group")) groups++;
                        else if (n.Contains("power")) pblocks++;
                    }
                }
            }

            ctx.totalParts = total;
            ctx.boilers = boilers;
            ctx.valves = valves;
            ctx.pumps = pumps;
            ctx.groupheads = groups;
            ctx.powerBlocks = pblocks;

            ctx.hasWaterRoute = waterLayer != null;
            ctx.hasPowerRoute = powerLayer != null;

            bool placing = (placement != null && placement.SelectedDef != null);
            ctx.inPlaceMode = placing;
            ctx.inInspectMode = !placing;

            return ctx;
        }
    }
}
