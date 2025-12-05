using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MachineRepair
{
    /// <summary>
    /// Provides simple controls to start/stop power and water propagation from the UI.
    /// Binds buttons to SimulationManager toggles and mirrors their current state.
    /// </summary>
    public class SimulationUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private SimulationManager simulationManager;
        [SerializeField] private Button startPowerButton;
        [SerializeField] private Button startWaterButton;
        [SerializeField] private TextMeshProUGUI powerLabel;
        [SerializeField] private TextMeshProUGUI waterLabel;

        [Header("Visuals")]
        [SerializeField] private Color activeColor = new Color(0.18f, 0.58f, 0.32f);
        [SerializeField] private Color inactiveColor = new Color(0.4f, 0.4f, 0.4f);

        private void Awake()
        {
            if (simulationManager == null)
            {
                simulationManager = FindFirstObjectByType<SimulationManager>();
            }
        }

        private void OnEnable()
        {
            if (startPowerButton != null) startPowerButton.onClick.AddListener(OnPowerClicked);
            if (startWaterButton != null) startWaterButton.onClick.AddListener(OnWaterClicked);

            if (simulationManager != null)
            {
                simulationManager.SimulationStepCompleted += OnSimulationStepCompleted;
                simulationManager.PowerToggled += OnPowerToggled;
                simulationManager.WaterToggled += OnWaterToggled;
            }

            UpdateStatus();
        }

        private void OnDisable()
        {
            if (startPowerButton != null) startPowerButton.onClick.RemoveListener(OnPowerClicked);
            if (startWaterButton != null) startWaterButton.onClick.RemoveListener(OnWaterClicked);

            if (simulationManager != null)
            {
                simulationManager.SimulationStepCompleted -= OnSimulationStepCompleted;
                simulationManager.PowerToggled -= OnPowerToggled;
                simulationManager.WaterToggled -= OnWaterToggled;
            }
        }

        private void OnPowerClicked()
        {
            if (simulationManager == null) return;

            simulationManager.SetPower(!simulationManager.PowerOn);
            simulationManager.RunSimulationStep();
        }

        private void OnWaterClicked()
        {
            if (simulationManager == null) return;

            simulationManager.SetWater(!simulationManager.WaterOn);
            simulationManager.RunSimulationStep();
        }

        private void OnSimulationStepCompleted()
        {
            UpdateStatus();
        }

        private void OnPowerToggled(bool state)
        {
            UpdateStatus();
        }

        private void OnWaterToggled(bool state)
        {
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            bool powerActive = simulationManager != null && simulationManager.PowerOn;
            bool waterActive = simulationManager != null && simulationManager.WaterOn;

            if (powerLabel != null)
            {
                powerLabel.text = powerActive ? "Power: ON" : "Power: OFF";
                powerLabel.color = powerActive ? activeColor : inactiveColor;
            }

            if (waterLabel != null)
            {
                waterLabel.text = waterActive ? "Water: ON" : "Water: OFF";
                waterLabel.color = waterActive ? activeColor : inactiveColor;
            }
        }
    }
}
