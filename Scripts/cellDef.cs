using UnityEngine;
using MachineRepair;

namespace MachineRepair.Grid
{
    [System.Serializable]
    public struct CellTerrain
    {
        public int index;
        public CellPlaceability placeability;
    }

    [System.Serializable]
    public struct CellOccupancy
    {
        public MachineComponent component;    // machine / fixture
        public PlacedWire wire;               // electrical
        public WireDef wireDef;               // wire definition
        public bool pipe;                     // plumbing

        public bool HasComponent => component != null;
        public bool HasWire => wire != null;
        public bool HasPipe => pipe;

        public void Clear()
        {
            component = null;
            wire = null;
            wireDef = null;
            pipe = false;
        }
    }

    [System.Serializable]
    public struct cellDef
    {
        public int index;
        public CellPlaceability placeability;

        // Contents of the cell:
        public MachineComponent component;    // machine / fixture
        public PlacedWire wire;               // placed wire data
        public WireDef wireDef;               // wire definition data
        public bool pipe;                     // plumbing

        // Convenience helpers
        public bool HasComponent => component != null;
        public bool HasWire => wire != null;
        public bool HasPipe => pipe;

        public static cellDef From(CellTerrain terrain, CellOccupancy occupancy)
        {
            return new cellDef
            {
                index = terrain.index,
                placeability = terrain.placeability,
                component = occupancy.component,
                wire = occupancy.wire,
                wireDef = occupancy.wireDef,
                pipe = occupancy.pipe
            };
        }
    };
}
