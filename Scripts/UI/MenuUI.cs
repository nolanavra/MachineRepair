using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MachineRepair
{
    /// <summary>
    /// Central menu controller that bridges high-level UI buttons to inventory,
    /// inspector, simulation, and mode controls.
    /// </summary>
    public class MenuUI : MonoBehaviour, IGameModeListener
    {
        [Header("References")]
        [SerializeField] private SimpleInventoryUI inventoryUI;
        [SerializeField] private InspectorUI inspectorUI;
        [SerializeField] private GameModeManager gameModeManager;
        [SerializeField] private SimulationUI simulationUI;
        [SerializeField] private SimulationManager simulationManager;

        [Header("Buttons")]
        [SerializeField] private Button inventoryButton;
        [SerializeField] private Button inspectorButton;
        [SerializeField] private Button wireModeButton;
        [SerializeField] private Button pipeModeButton;
        [SerializeField] private Button powerToggleButton;
        [SerializeField] private Button waterToggleButton;
        [SerializeField] private Button viewSwitchButton;

        [Header("Labels")]        
        [SerializeField] private TextMeshProUGUI powerButtonLabel;
        [SerializeField] private TextMeshProUGUI waterButtonLabel;
        [SerializeField] private TextMeshProUGUI modeLabel;

        [Header("Mode Cycling")]
        [SerializeField] private GameMode[] modeCycle =
        {
            GameMode.Selection,
            GameMode.WirePlacement,
            GameMode.PipePlacement
        };
        [SerializeField] private UnityEvent viewSwitchRequested;

        [Header("Appearance")]
        [SerializeField] private Color modeSelectedColor = new(0.8f, 0.9f, 1f, 1f);
        [SerializeField] private Color modeDefaultColor = Color.white;

        private void Awake()
        {
            if (gameModeManager == null) gameModeManager = GameModeManager.Instance;
            if (simulationManager == null) simulationManager = FindFirstObjectByType<SimulationManager>();
            if (simulationUI == null) simulationUI = FindFirstObjectByType<SimulationUI>();
            if (inventoryUI == null) inventoryUI = FindFirstObjectByType<SimpleInventoryUI>();
            if (inspectorUI == null) inspectorUI = FindFirstObjectByType<InspectorUI>();
        }

        private void OnEnable()
        {
            RegisterButtonListeners();
            RegisterModeListeners();
            RegisterSimulationListeners();

            UpdateModeIndicators(gameModeManager != null ? gameModeManager.CurrentMode : default);
            UpdatePowerLabel(simulationManager != null && simulationManager.PowerOn);
            UpdateWaterLabel(simulationManager != null && simulationManager.WaterOn);
        }

        private void OnDisable()
        {
            UnregisterButtonListeners();
            UnregisterModeListeners();
            UnregisterSimulationListeners();
        }

        public void OnEnterMode(GameMode newMode)
        {
            UpdateModeIndicators(newMode);
        }

        public void OnExitMode(GameMode oldMode)
        {
        }

        private void RegisterButtonListeners()
        {
            if (inventoryButton != null) inventoryButton.onClick.AddListener(OnInventoryClicked);
            if (inspectorButton != null) inspectorButton.onClick.AddListener(OnInspectorClicked);
            if (wireModeButton != null) wireModeButton.onClick.AddListener(SetWireMode);
            if (pipeModeButton != null) pipeModeButton.onClick.AddListener(SetPipeMode);
            if (powerToggleButton != null) powerToggleButton.onClick.AddListener(TogglePower);
            if (waterToggleButton != null) waterToggleButton.onClick.AddListener(ToggleWater);
            if (viewSwitchButton != null) viewSwitchButton.onClick.AddListener(OnViewSwitchClicked);
        }

        private void UnregisterButtonListeners()
        {
            if (inventoryButton != null) inventoryButton.onClick.RemoveListener(OnInventoryClicked);
            if (inspectorButton != null) inspectorButton.onClick.RemoveListener(OnInspectorClicked);
            if (wireModeButton != null) wireModeButton.onClick.RemoveListener(SetWireMode);
            if (pipeModeButton != null) pipeModeButton.onClick.RemoveListener(SetPipeMode);
            if (powerToggleButton != null) powerToggleButton.onClick.RemoveListener(TogglePower);
            if (waterToggleButton != null) waterToggleButton.onClick.RemoveListener(ToggleWater);
            if (viewSwitchButton != null) viewSwitchButton.onClick.RemoveListener(OnViewSwitchClicked);
        }

        private void RegisterModeListeners()
        {
            if (gameModeManager != null)
            {
                gameModeManager.RegisterListener(this);
            }
        }

        private void UnregisterModeListeners()
        {
            if (gameModeManager != null)
            {
                gameModeManager.UnregisterListener(this);
            }
        }

        private void RegisterSimulationListeners()
        {
            if (simulationManager == null) return;

            simulationManager.PowerToggled += OnPowerToggled;
            simulationManager.WaterToggled += OnWaterToggled;
        }

        private void UnregisterSimulationListeners()
        {
            if (simulationManager == null) return;

            simulationManager.PowerToggled -= OnPowerToggled;
            simulationManager.WaterToggled -= OnWaterToggled;
        }

        private void OnInventoryClicked()
        {
            inventoryUI?.ShowHideInventory();
        }

        private void OnInspectorClicked()
        {
            inspectorUI?.ToggleInspector();
        }

        private void SetWireMode()
        {
            gameModeManager?.SetMode(GameMode.WirePlacement);
        }

        private void SetPipeMode()
        {
            gameModeManager?.SetMode(GameMode.PipePlacement);
        }

        private void TogglePower()
        {
            if (simulationManager == null) return;

            simulationManager.SetPower(!simulationManager.PowerOn);
            simulationManager.RunSimulationStep();
        }

        private void ToggleWater()
        {
            if (simulationManager == null) return;

            simulationManager.SetWater(!simulationManager.WaterOn);
            simulationManager.RunSimulationStep();
        }

        private void OnViewSwitchClicked()
        {
            if (viewSwitchRequested != null && viewSwitchRequested.GetPersistentEventCount() > 0)
            {
                viewSwitchRequested.Invoke();
                return;
            }

            CycleMode();
        }

        private void CycleMode()
        {
            if (gameModeManager == null || modeCycle == null || modeCycle.Length == 0)
                return;

            GameMode current = gameModeManager.CurrentMode;
            int currentIndex = Array.IndexOf(modeCycle, current);
            int nextIndex = currentIndex >= 0 ? (currentIndex + 1) % modeCycle.Length : 0;

            gameModeManager.SetMode(modeCycle[nextIndex]);
        }

        private void OnPowerToggled(bool state)
        {
            UpdatePowerLabel(state);
        }

        private void OnWaterToggled(bool state)
        {
            UpdateWaterLabel(state);
        }

        private void UpdatePowerLabel(bool powerOn)
        {
            if (powerButtonLabel != null)
            {
                powerButtonLabel.text = powerOn ? "Power: ON" : "Power: OFF";
            }
        }

        private void UpdateWaterLabel(bool waterOn)
        {
            if (waterButtonLabel != null)
            {
                waterButtonLabel.text = waterOn ? "Water: ON" : "Water: OFF";
            }
        }

        private void UpdateModeIndicators(GameMode currentMode)
        {
            HighlightButton(wireModeButton, currentMode == GameMode.WirePlacement);
            HighlightButton(pipeModeButton, currentMode == GameMode.PipePlacement);

            if (modeLabel != null)
            {
                modeLabel.text = GameModeManager.ModeToDisplay(currentMode);
            }
        }

        private void HighlightButton(Button button, bool selected)
        {
            if (button == null || button.targetGraphic == null)
                return;

            button.targetGraphic.color = selected ? modeSelectedColor : modeDefaultColor;
        }
    }
}
