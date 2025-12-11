using MachineRepair;
using MachineRepair.Grid;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MachineRepair.Grid {

    public class DebugUI : MonoBehaviour, IGameModeListener
    {
        [SerializeField] private Camera cam;
        [SerializeField] private GridManager grid;
        [SerializeField] private InputRouter router;
        [SerializeField] private GameModeManager gameModeManager;
        [Header("Panel")]
        [SerializeField] private GameObject debugPanel;
        [SerializeField] private bool panelVisible = true;
        [Header("Input")]
        [SerializeField] private PlayerInput playerInput;
        [SerializeField] private string gameplayMapName = "Gameplay";
        [SerializeField] private string toggleDebugActionName = "ToggleDebugUI";
        [SerializeField] private TextMeshProUGUI cellText;
        [SerializeField] private TextMeshProUGUI cellOccupancy;
        [SerializeField] private TextMeshProUGUI gameMode;
        [Header("Simulation")]
        [SerializeField] private SimulationManager simulationManager;
        [SerializeField] private TextMeshProUGUI waterConditions;
        [SerializeField] private TextMeshProUGUI simulationStepLabel;

        private InputAction toggleDebugAction;
        private CanvasGroup panelCanvasGroup;

        private void OnEnable()
        {
            if (GameModeManager.Instance != null)
                GameModeManager.Instance.RegisterListener(this);

            SubscribeSimulationEvents();
            CacheInputActions();
            EnableInput();
            ApplyPanelVisibility();
            UpdateSimulationStepLabel();
        }

        private void OnDisable()
        {
            if (GameModeManager.Instance != null)
                GameModeManager.Instance.UnregisterListener(this);

            UnsubscribeSimulationEvents();
            DisableInput();
        }

        void Awake()
        {
            cam = Camera.main;

            // Updated method  no warnings
            grid = Object.FindFirstObjectByType<GridManager>();

            if (simulationManager == null)
            {
                simulationManager = Object.FindFirstObjectByType<SimulationManager>();
            }

            if (debugPanel == null)
            {
                debugPanel = gameObject;
            }
        }

        void Update()
        {
            //int cellCount = grid.CellCount;
            Vector2Int location = router.GetMousePos();
            int index = 0;
            if (grid.InBounds(location.x, location.y) && grid.setup)
            {
                index = CellIndex.ToIndex(location.x, location.y, grid.width);

                var cell = grid.GetCell(location);

                cellText.text = $"({location.x}, {location.y})  | i={index} Placeability: {cell.placeability}";
                cellOccupancy.text = $"Contents// Comp:{cell.HasComponent} Wire: {cell.HasWire} Pipe: {cell.HasPipe} ";

                UpdateWaterDebug(index);


            }
            else
            {
                cellText.text = $"(out of bounds)";
                cellOccupancy.text = $"---";
                if (waterConditions != null) waterConditions.text = "Water: ---";
            }

        }

        // -------------- IGameModeListener ----------------

        public void OnEnterMode(GameMode newMode)
        {
            gameMode.text = $"Mode Selected: {newMode.ToString()} ";
        }

        public void OnExitMode(GameMode oldMode)
        {
            // Optional: cleanup when leaving a mode (e.g., cancel wire run)
            // Example:
            // if (oldMode == GameMode.WirePlacement) WireTool.CancelIfIncomplete();
        }

        private void UpdateWaterDebug(int cellIndex)
        {
            if (waterConditions == null)
            {
                return;
            }

            if (simulationManager == null || !simulationManager.LastSnapshot.HasValue)
            {
                waterConditions.text = "Water: snapshot unavailable";
                return;
            }

            var snapshot = simulationManager.LastSnapshot.Value;
            float pressure = (snapshot.Pressure != null && cellIndex >= 0 && cellIndex < snapshot.Pressure.Length)
                ? snapshot.Pressure[cellIndex]
                : 0f;
            float flow = (snapshot.Flow != null && cellIndex >= 0 && cellIndex < snapshot.Flow.Length)
                ? snapshot.Flow[cellIndex]
                : 0f;

            string state = simulationManager.WaterOn ? "ON" : "OFF";
            string runState = simulationManager.SimulationRunning ? "Running" : "Paused";

            waterConditions.text = $"Water: {state} ({runState})\nPressure: {pressure:0.###} | Flow: {flow:0.###}";
        }

        private void SubscribeSimulationEvents()
        {
            if (simulationManager == null) return;

            simulationManager.SimulationStepCompleted += OnSimulationStepCompleted;
        }

        private void UnsubscribeSimulationEvents()
        {
            if (simulationManager == null) return;

            simulationManager.SimulationStepCompleted -= OnSimulationStepCompleted;
        }

        private void OnSimulationStepCompleted()
        {
            UpdateSimulationStepLabel();
        }

        private void UpdateSimulationStepLabel()
        {
            if (simulationStepLabel == null)
            {
                return;
            }

            if (simulationManager != null && simulationManager.LastSnapshot.HasValue)
            {
                simulationStepLabel.text = $"Simulation Steps: {simulationManager.LastSnapshot.Value.StepIndex}";
            }
            else if (simulationManager != null)
            {
                simulationStepLabel.text = $"Simulation Steps: {simulationManager.SimulationStepCount}";
            }
            else
            {
                simulationStepLabel.text = "Simulation Steps: ---";
            }
        }

        private void CacheInputActions()
        {
            if (playerInput == null)
            {
                playerInput = Object.FindFirstObjectByType<PlayerInput>();
            }

            if (playerInput == null || playerInput.actions == null) return;

            var map = playerInput.actions.FindActionMap(gameplayMapName, throwIfNotFound: false);
            if (map == null) return;

            toggleDebugAction = map.FindAction(toggleDebugActionName, throwIfNotFound: false);
        }

        private void EnableInput()
        {
            if (toggleDebugAction == null) return;

            toggleDebugAction.performed += OnToggleDebugAction;
            toggleDebugAction.Enable();
        }

        private void DisableInput()
        {
            if (toggleDebugAction == null) return;

            toggleDebugAction.performed -= OnToggleDebugAction;
            toggleDebugAction.Disable();
        }

        private void OnToggleDebugAction(InputAction.CallbackContext ctx)
        {
            if (!ctx.performed) return;

            panelVisible = !panelVisible;
            ApplyPanelVisibility();
        }

        private void ApplyPanelVisibility()
        {
            if (debugPanel == null)
            {
                return;
            }

            if (panelCanvasGroup == null)
            {
                panelCanvasGroup = debugPanel.GetComponent<CanvasGroup>();
                if (panelCanvasGroup == null)
                {
                    panelCanvasGroup = debugPanel.AddComponent<CanvasGroup>();
                }
            }

            panelCanvasGroup.alpha = panelVisible ? 1f : 0f;
            panelCanvasGroup.interactable = panelVisible;
            panelCanvasGroup.blocksRaycasts = panelVisible;
        }
    }
}
