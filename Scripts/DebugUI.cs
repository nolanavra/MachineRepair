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

                UpdateWaterDebug(index, location, cell);


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

        private void UpdateWaterDebug(int cellIndex, Vector2Int cellPosition, cellDef cell)
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

            bool hasWaterPort = TryGetWaterPortCell(cell, cellPosition, out var portCell);
            float portPressure = pressure;
            float portFlow = flow;
            if (hasWaterPort && snapshot.TryGetPortHydraulicState(portCell, out var portState))
            {
                portPressure = portState.Pressure_Pa;
                portFlow = portState.Flow_m3s;
            }

            string portLine = hasWaterPort
                ? $"Port Pressure/Flow: {portPressure:0.###} | {portFlow:0.###}"
                : "Port Pressure/Flow: (no water port)";

            waterConditions.text =
                $"Water: {state} ({runState})\nCell Pressure/Flow: {pressure:0.###} | {flow:0.###}\n{portLine}";
        }

        private static bool TryGetWaterPortCell(cellDef cell, Vector2Int hoveredCell, out Vector2Int portCell)
        {
            portCell = default;

            var component = cell.component;
            if (component == null || component.def == null || component.portDef == null || !component.def.water)
            {
                return false;
            }

            var ports = component.portDef.ports;
            if (ports == null || ports.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < ports.Length; i++)
            {
                var port = ports[i];
                if (port.portType != PortType.Water)
                {
                    continue;
                }

                var globalPortCell = component.GetGlobalCell(port);
                if (globalPortCell == hoveredCell)
                {
                    portCell = globalPortCell;
                    return true;
                }
            }

            return false;
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
