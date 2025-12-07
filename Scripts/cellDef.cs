using System;
using System.Collections.Generic;
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
        public List<PlacedWire> wires;        // electrical (supports multiple runs)
        public bool pipe;                     // plumbing

        public bool HasComponent => component != null;
        public bool HasWire => wires != null && wires.Count > 0;
        public bool HasPipe => pipe;
        public PlacedWire PrimaryWire => HasWire ? wires[0] : null;
        public WireDef PrimaryWireDef => PrimaryWire != null ? PrimaryWire.wireDef : null;
        public IReadOnlyList<PlacedWire> Wires => wires != null ? (IReadOnlyList<PlacedWire>)wires : Array.Empty<PlacedWire>();

        public void AddWire(PlacedWire wire)
        {
            if (wire == null) return;
            wires ??= new List<PlacedWire>();
            if (!wires.Contains(wire))
            {
                wires.Add(wire);
            }
        }

        public void Clear()
        {
            component = null;
            wires = null;
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
        public List<PlacedWire> wires;        // placed wire data
        public bool pipe;                     // plumbing

        // Convenience helpers
        public bool HasComponent => component != null;
        public bool HasWire => wires != null && wires.Count > 0;
        public bool HasPipe => pipe;
        public PlacedWire PrimaryWire => HasWire ? wires[0] : null;
        public WireDef PrimaryWireDef => PrimaryWire != null ? PrimaryWire.wireDef : null;
        public IReadOnlyList<PlacedWire> Wires => wires != null ? (IReadOnlyList<PlacedWire>)wires : Array.Empty<PlacedWire>();

        public PlacedWire GetWireAt(int index)
        {
            if (!HasWire || index < 0 || index >= wires.Count) return null;
            return wires[index];
        }

        public static cellDef From(CellTerrain terrain, CellOccupancy occupancy)
        {
            return new cellDef
            {
                index = terrain.index,
                placeability = terrain.placeability,
                component = occupancy.component,
                wires = occupancy.HasWire ? new List<PlacedWire>(occupancy.Wires) : null,
                pipe = occupancy.pipe
            };
        }
    };
}
