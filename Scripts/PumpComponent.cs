using UnityEngine;

namespace MachineRepair
{
    /// <summary>
    /// Simple water pump that raises downstream pressure once the component is
    /// filled (and, optionally, powered). Values are serialized for authoring on
    /// the prefab; GetOutputPressure applies the configured boost while clamping
    /// against the incoming pressure so we never drop the line by accident.
    /// </summary>
    [RequireComponent(typeof(MachineComponent))]
    public class PumpComponent : MonoBehaviour
    {
        [Header("Pressure")]
        [SerializeField, Min(0f)] private float outputPressure = 10f;
        [SerializeField, Tooltip("Optional additive boost applied on top of the incoming pressure.")]
        private float pressureBoost = 0f;
        [Header("Power")]
        [SerializeField, Tooltip("If true, the pump will only boost when powered.")]
        private bool requiresPower = true;

        private MachineComponent machineComponent;

        public bool RequiresPower => requiresPower;

        private void Reset()
        {
            machineComponent = GetComponent<MachineComponent>();
        }

        private void Awake()
        {
            if (machineComponent == null)
            {
                machineComponent = GetComponent<MachineComponent>();
            }
        }

        /// <summary>
        /// Returns the target downstream pressure, honoring configured boost and
        /// power requirements. The pump never reduces pressure; at minimum it
        /// echoes the incoming value.
        /// </summary>
        public float GetOutputPressure(float incomingPressure)
        {
            bool powered = !requiresPower || (machineComponent != null && machineComponent.IsPowered);
            if (!powered)
            {
                return Mathf.Max(0f, incomingPressure);
            }

            float boosted = Mathf.Max(0f, incomingPressure + Mathf.Max(0f, pressureBoost));
            float target = Mathf.Max(boosted, outputPressure);
            return Mathf.Max(target, incomingPressure);
        }
    }
}
