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
        public bool isDisplayZone;
    }

    [System.Serializable]
    public struct CellOccupancy
    {
        public MachineComponent component;    // machine / fixture
        public List<PlacedWire> wires;        // electrical (supports multiple runs)
        public List<PlacedPipe> pipes;        // plumbing (supports multiple runs)

        public bool HasComponent => component != null;
        public bool HasWire => wires != null && wires.Count > 0;
        public bool HasPipe => pipes != null && pipes.Count > 0;
        public PlacedWire PrimaryWire => HasWire ? wires[0] : null;
        public WireDef PrimaryWireDef => PrimaryWire != null ? PrimaryWire.wireDef : null;
        public IReadOnlyList<PlacedWire> Wires => wires != null ? (IReadOnlyList<PlacedWire>)wires : Array.Empty<PlacedWire>();
        public IReadOnlyList<PlacedPipe> Pipes => pipes != null ? (IReadOnlyList<PlacedPipe>)pipes : Array.Empty<PlacedPipe>();

        public void AddWire(PlacedWire wire)
        {
            if (wire == null) return;
            wires ??= new List<PlacedWire>();
            if (!wires.Contains(wire))
            {
                wires.Add(wire);
            }
        }

        public void AddPipe(PlacedPipe pipe)
        {
            if (pipe == null) return;
            pipes ??= new List<PlacedPipe>();
            if (!pipes.Contains(pipe))
            {
                pipes.Add(pipe);
            }
        }

        public void Clear()
        {
            component = null;
            wires = null;
            pipes = null;
        }
    }

    [System.Serializable]
    public struct cellDef
    {
        public int index;
        public CellPlaceability placeability;
        public bool isDisplayZone;

        // Contents of the cell:
        public MachineComponent component;    // machine / fixture
        public List<PlacedWire> wires;        // placed wire data
        public List<PlacedPipe> pipes;        // plumbing

        // Convenience helpers
        public bool HasComponent => component != null;
        public bool HasWire => wires != null && wires.Count > 0;
        public bool HasPipe => pipes != null && pipes.Count > 0;
        public PlacedWire PrimaryWire => HasWire ? wires[0] : null;
        public WireDef PrimaryWireDef => PrimaryWire != null ? PrimaryWire.wireDef : null;
        public PlacedPipe PrimaryPipe => HasPipe ? pipes[0] : null;
        public IReadOnlyList<PlacedWire> Wires => wires != null ? (IReadOnlyList<PlacedWire>)wires : Array.Empty<PlacedWire>();
        public IReadOnlyList<PlacedPipe> Pipes => pipes != null ? (IReadOnlyList<PlacedPipe>)pipes : Array.Empty<PlacedPipe>();

        public PlacedWire GetWireAt(int index)
        {
            if (!HasWire || index < 0 || index >= wires.Count) return null;
            return wires[index];
        }

        public PlacedPipe GetPipeAt(int index)
        {
            if (!HasPipe || index < 0 || index >= pipes.Count) return null;
            return pipes[index];
        }

        public static cellDef From(CellTerrain terrain, CellOccupancy occupancy)
        {
            return new cellDef
            {
                index = terrain.index,
                placeability = terrain.placeability,
                isDisplayZone = terrain.isDisplayZone,
                component = occupancy.component,
                wires = occupancy.HasWire ? new List<PlacedWire>(occupancy.Wires) : null,
                pipes = occupancy.HasPipe ? new List<PlacedPipe>(occupancy.Pipes) : null
            };
        }
    };
}
