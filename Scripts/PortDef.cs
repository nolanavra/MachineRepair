using MachineRepair.Grid;
using System;
using UnityEngine;

namespace MachineRepair
{
    [Serializable]
    public struct PortLocal
    {
        public Vector2Int cell;
        public PortType port;
        public bool isInput;

        public Vector2Int ToGlobalCell(Vector2Int anchor, int rotation)
        {
            return ToGlobalCell(anchor, rotation, Vector2Int.zero);
        }

        public Vector2Int ToGlobalCell(Vector2Int anchor, int rotation, Vector2Int footprintOrigin)
        {
            return anchor + GetRotatedOffset(rotation, footprintOrigin);
        }

        public Vector2Int GetRotatedOffset(int rotation)
        {
            return GetRotatedOffset(rotation, Vector2Int.zero);
        }

        public Vector2Int GetRotatedOffset(int rotation, Vector2Int footprintOrigin)
        {
            Vector2Int local = cell - footprintOrigin;

            return rotation switch
            {
                1 => new Vector2Int(local.y, -local.x),
                2 => new Vector2Int(-local.x, -local.y),
                3 => new Vector2Int(-local.y, local.x),
                _ => local
            };
        }
    }

    [CreateAssetMenu(menuName = "Espresso/Port Set")]
    public class PortDef : ScriptableObject
    {
        public PortLocal[] ports;
    }
}
