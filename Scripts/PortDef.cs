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
            return anchor + GetRotatedOffset(rotation);
        }

        public Vector2Int GetRotatedOffset(int rotation)
        {
            return rotation switch
            {
                1 => new Vector2Int(cell.y, -cell.x),
                2 => new Vector2Int(-cell.x, -cell.y),
                3 => new Vector2Int(-cell.y, cell.x),
                _ => cell
            };
        }
    }

    [CreateAssetMenu(menuName = "Espresso/Port Set")]
    public class PortDef : ScriptableObject
    {
        public PortLocal[] ports;
    }
}
