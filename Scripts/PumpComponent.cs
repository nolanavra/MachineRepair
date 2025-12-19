using UnityEngine;

namespace MachineRepair
{
    /// <summary>
    /// Configurable pump that raises incoming water pressure when powered and full.
    /// </summary>
    [RequireComponent(typeof(MachineComponent))]
    public class PumpComponent : MonoBehaviour
    {
        [Header("Pressure")]
        [SerializeField] [Tooltip("Absolute pressure value to output when boosting.")]
        private float outputPressure = 9f;
        [SerializeField] [Tooltip("Additive pressure delta when using boost mode.")]
        private float pressureBoost = 0f;
        [SerializeField] [Tooltip("When true, the pump targets OutputPressure; when false, it adds PressureBoost to the incoming pressure.")]
        private bool useAbsoluteOutput = true;

        [Header("Dependencies")]
        [SerializeField] [Tooltip("Whether the pump must be powered before applying a boost.")]
        private bool requiresPower = true;

        public float OutputPressure
        {
            get => outputPressure;
            set => outputPressure = Mathf.Max(0f, value);
        }

        public float PressureBoost
        {
            get => pressureBoost;
            set => pressureBoost = Mathf.Max(0f, value);
        }

        public bool UseAbsoluteOutput
        {
            get => useAbsoluteOutput;
            set => useAbsoluteOutput = value;
        }

        public bool RequiresPower
        {
            get => requiresPower;
            set => requiresPower = value;
        }

        /// <summary>
        /// Returns the outgoing pressure after applying the configured boost profile.
        /// </summary>
        public float GetOutputPressure(float incomingPressure)
        {
            float baseline = Mathf.Max(0f, incomingPressure);
            float configured = useAbsoluteOutput
                ? Mathf.Max(0f, outputPressure)
                : baseline + Mathf.Max(0f, pressureBoost);

            return Mathf.Max(baseline, configured);
        }
    }
}
