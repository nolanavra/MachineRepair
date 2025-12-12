using System;
using UnityEngine;

namespace MachineRepair
{
    /// <summary>
    /// Components can implement this to provide conditional connectivity between their ports.
    /// </summary>
    public interface IConditionalPortLink
    {
        /// <summary>
        /// Returns true when the connection between the provided port pair should be included in the power graph.
        /// </summary>
        bool AllowsConnection(PortLocal from, int fromIndex, PortLocal to, int toIndex);
    }

    /// <summary>
    /// Simple two-terminal switch that conditionally bridges its two power ports.
    /// </summary>
    [RequireComponent(typeof(MachineComponent))]
    public class SwitchComponent : MonoBehaviour, IConditionalPortLink
    {
        [Header("Ports")]
        [SerializeField] private int firstPortIndex = 0;
        [SerializeField] private int secondPortIndex = 1;
        [SerializeField] private bool startClosed = true;

        [Header("Animation")]
        [SerializeField] private Animator animator;
        [SerializeField] private string closedBoolParameter = "Closed";

        [Header("Indicator")]
        [SerializeField] private SpriteRenderer indicatorRenderer;
        [SerializeField] private Color closedColor = new Color(0.3f, 0.95f, 0.5f, 1f);
        [SerializeField] private Color openColor = new Color(1f, 0.45f, 0.45f, 1f);
        [SerializeField] private string indicatorSortingLayer = "WireGlow";
        [SerializeField] private int indicatorSortingOrder = 250;

        [Header("State (read-only)")]
        [SerializeField] private bool isClosed;

        public bool IsClosed => isClosed;
        public bool IsOpen => !isClosed;
        public bool DefaultClosedState { get => startClosed; set => startClosed = value; }

        public event Action<bool> StateChanged;

        private MachineComponent machineComponent;

        private void Awake()
        {
            if (machineComponent == null) machineComponent = GetComponent<MachineComponent>();
        }

        private void OnEnable()
        {
            ApplyConfiguredState();
        }

        /// <summary>
        /// Reapplies the serialized default state. Useful when toggled via UI or freshly spawned.
        /// </summary>
        public void ApplyConfiguredState()
        {
            SetState(startClosed, force: true);
        }

        public void Toggle()
        {
            SetState(!isClosed);
        }

        public void SetState(bool closed)
        {
            SetState(closed, force: false);
        }

        private void SetState(bool closed, bool force)
        {
            if (!force && closed == isClosed) return;

            isClosed = closed;
            ApplyAnimatorState();
            UpdateIndicator();
            StateChanged?.Invoke(isClosed);
        }

        public bool AllowsConnection(PortLocal from, int fromIndex, PortLocal to, int toIndex)
        {
            if (!IsConfiguredPair(fromIndex, toIndex))
            {
                return true;
            }

            return isClosed;
        }

        private bool IsConfiguredPair(int fromIndex, int toIndex)
        {
            if (machineComponent == null || machineComponent.portDef == null || machineComponent.portDef.ports == null)
            {
                return false;
            }

            bool matchesForward = firstPortIndex == fromIndex && secondPortIndex == toIndex;
            bool matchesReverse = firstPortIndex == toIndex && secondPortIndex == fromIndex;
            return matchesForward || matchesReverse;
        }

        private void ApplyAnimatorState()
        {
            if (animator == null || string.IsNullOrWhiteSpace(closedBoolParameter)) return;
            animator.SetBool(closedBoolParameter, isClosed);
        }

        private void UpdateIndicator()
        {
            if (indicatorRenderer == null) return;

            indicatorRenderer.sortingLayerName = indicatorSortingLayer;
            indicatorRenderer.sortingOrder = indicatorSortingOrder;

            int glowLayer = LayerMask.NameToLayer(indicatorSortingLayer);
            if (glowLayer >= 0)
            {
                indicatorRenderer.gameObject.layer = glowLayer;
            }

            indicatorRenderer.color = isClosed ? closedColor : openColor;
        }
    }
}

