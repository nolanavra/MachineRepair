using MachineRepair.Grid;
using UnityEngine;

namespace MachineRepair {
    public enum ComponentType{
        None,
        ChassisPowerConnection,
        ChassisWaterConnection,
        Boiler,
        Pump,
        Grouphead,
        Controler,
        SolonoidValve,
        FlowRestrictor
    }

    [RequireComponent(typeof(Transform))]
    public class MachineComponent : MonoBehaviour {
        [Header("References")]
        [SerializeField] public ThingDef def;
        [SerializeField] public GridManager grid;
        public FootprintMask footprint;
        public int rotation;
        public Vector2Int anchorCell;
        public PortDef portDef;

        public Vector2Int GetGlobalCell(Vector2Int localCell)
        {
            return anchorCell + RotateOffset(localCell, rotation);
        }

        public Vector2Int GetGlobalCell(PortLocal port)
        {
            return port.ToGlobalCell(anchorCell, rotation);
        }

        private static Vector2Int RotateOffset(Vector2Int offset, int rotationSteps)
        {
            return rotationSteps switch
            {
                1 => new Vector2Int(offset.y, -offset.x),
                2 => new Vector2Int(-offset.x, -offset.y),
                3 => new Vector2Int(-offset.y, offset.x),
                _ => offset
            };
        }
    }

}

