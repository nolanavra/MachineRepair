using UnityEngine;
using UnityEngine.UI;

namespace MachineRepair
{
    /// <summary>
    /// Provides quick buttons to enter wire or pipe placement modes and, when
    /// in wire placement, exposes sub-options for choosing the wire type.
    /// </summary>
    public class WirePipeUI : MonoBehaviour, IGameModeListener
    {
        [Header("References")]
        [SerializeField] private GameModeManager gameModeManager;
        [SerializeField] private WirePlacementTool wirePlacementTool;

        [Header("Panels")]
        [SerializeField] private GameObject wireTypePanel;

        [Header("Mode Buttons")]
        [SerializeField] private Button wireModeButton;
        [SerializeField] private Button pipeModeButton;

        [Header("Wire Type Buttons")]
        [SerializeField] private Button standardWireButton;
        [SerializeField] private Button signalWireButton;

        private void Awake()
        {
            if (gameModeManager == null) gameModeManager = GameModeManager.Instance;
            if (wirePlacementTool == null) wirePlacementTool = FindFirstObjectByType<WirePlacementTool>();
        }

        private void OnEnable()
        {
            if (gameModeManager != null)
            {
                gameModeManager.RegisterListener(this);
                UpdateWireTypePanel(gameModeManager.CurrentMode == GameMode.WirePlacement);
            }

            if (wireModeButton != null) wireModeButton.onClick.AddListener(SetWireMode);
            if (pipeModeButton != null) pipeModeButton.onClick.AddListener(SetPipeMode);
            if (standardWireButton != null) standardWireButton.onClick.AddListener(SetStandardWire);
            if (signalWireButton != null) signalWireButton.onClick.AddListener(SetSignalWire);
        }

        private void OnDisable()
        {
            if (gameModeManager != null)
            {
                gameModeManager.UnregisterListener(this);
            }

            if (wireModeButton != null) wireModeButton.onClick.RemoveListener(SetWireMode);
            if (pipeModeButton != null) pipeModeButton.onClick.RemoveListener(SetPipeMode);
            if (standardWireButton != null) standardWireButton.onClick.RemoveListener(SetStandardWire);
            if (signalWireButton != null) signalWireButton.onClick.RemoveListener(SetSignalWire);
        }

        public void OnEnterMode(GameMode newMode)
        {
            UpdateWireTypePanel(newMode == GameMode.WirePlacement);
        }

        public void OnExitMode(GameMode oldMode)
        {
            if (oldMode == GameMode.WirePlacement)
            {
                UpdateWireTypePanel(false);
            }
        }

        private void SetWireMode()
        {
            gameModeManager?.SetMode(GameMode.WirePlacement);
        }

        private void SetPipeMode()
        {
            gameModeManager?.SetMode(GameMode.PipePlacement);
        }

        private void SetStandardWire()
        {
            if (wirePlacementTool == null) return;
            wirePlacementTool.SetWireType(WireType.AC);
        }

        private void SetSignalWire()
        {
            if (wirePlacementTool == null) return;
            wirePlacementTool.SetWireType(WireType.Signal);
        }

        private void UpdateWireTypePanel(bool visible)
        {
            if (wireTypePanel != null)
            {
                wireTypePanel.SetActive(visible);
            }
        }
    }
}
