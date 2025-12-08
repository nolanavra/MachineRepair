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
        [SerializeField] private PipePlacementTool pipePlacementTool;

        [Header("Panels")]
        [SerializeField] private GameObject wireTypePanel;

        [Header("Mode Buttons")]
        [SerializeField] private Button wireModeButton;
        [SerializeField] private Button pipeModeButton;

        [Header("Wire Type Buttons")]
        [SerializeField] private Button standardWireButton;
        [SerializeField] private Button signalWireButton;

        [Header("Pipe Appearance")]
        [SerializeField] private Image pipePreviewSwatch;
        [SerializeField] private Color pipePreviewColor = new Color(0.75f, 0.55f, 1f, 1f);
        [SerializeField] private float pipePreviewWidth = 0.07f;

        private void Awake()
        {
            if (gameModeManager == null) gameModeManager = GameModeManager.Instance;
            if (wirePlacementTool == null) wirePlacementTool = FindFirstObjectByType<WirePlacementTool>();
            if (pipePlacementTool == null) pipePlacementTool = FindFirstObjectByType<PipePlacementTool>();
            SyncPipePreviewDefaultsFromTool();
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

            ApplyPipeAppearance();
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
            if (pipePreviewSwatch != null)
            {
                pipePreviewSwatch.enabled = newMode == GameMode.PipePlacement;
            }
        }

        public void OnExitMode(GameMode oldMode)
        {
            if (oldMode == GameMode.WirePlacement)
            {
                UpdateWireTypePanel(false);
            }

            if (oldMode == GameMode.PipePlacement && pipePreviewSwatch != null)
            {
                pipePreviewSwatch.enabled = false;
            }
        }

        private void SetWireMode()
        {
            gameModeManager?.SetMode(GameMode.WirePlacement);
        }

        private void SetPipeMode()
        {
            gameModeManager?.SetMode(GameMode.PipePlacement);
            ApplyPipeAppearance();
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

        private void ApplyPipeAppearance()
        {
            if (pipePlacementTool != null)
            {
                pipePlacementTool.SetPipeColor(pipePreviewColor);
                pipePlacementTool.SetPipeWidth(pipePreviewWidth);
            }

            if (pipePreviewSwatch != null)
            {
                pipePreviewSwatch.color = pipePreviewColor;
                var size = pipePreviewSwatch.rectTransform.sizeDelta;
                pipePreviewSwatch.rectTransform.sizeDelta = new Vector2(Mathf.Max(size.x, 24f), Mathf.Max(pipePreviewWidth * 300f, 4f));
            }
        }

        private void SyncPipePreviewDefaultsFromTool()
        {
            if (pipePlacementTool == null) return;

            pipePreviewColor = pipePlacementTool.PipeColor;
            pipePreviewWidth = pipePlacementTool.PipeLineWidth;
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
