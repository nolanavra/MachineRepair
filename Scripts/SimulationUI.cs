using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using MachineRepair.Grid;

namespace MachineRepair
{
    /// <summary>
    /// Provides simple controls to start/stop power and water propagation from the UI.
    /// Binds buttons to SimulationManager toggles and mirrors their current state.
    /// </summary>
    public class SimulationUI : MonoBehaviour
        , IGameModeListener
    {
        [Header("References")]
        [SerializeField] private SimulationManager simulationManager;
        [SerializeField] private GridManager gridManager;
        [SerializeField] private GameModeManager gameModeManager;
        [Tooltip("Root panel GameObject that contains all Simulation UI controls.")]
        [SerializeField] private GameObject simulationPanel;
        [SerializeField] private Button startPowerButton;
        [SerializeField] private Button startWaterButton;
        [SerializeField] private TextMeshProUGUI powerLabel;
        [SerializeField] private TextMeshProUGUI waterLabel;

        [Header("Water Overlay")]
        [Tooltip("Arrow sprite used to visualize water flow across pipes.")]
        [SerializeField] private SpriteRenderer pipeArrowPrefab;
        [SerializeField] private Transform pipeArrowParent;
        [SerializeField] private float arrowTravelDistance = 0.35f;
        [SerializeField] private float arrowScrollSpeed = 1.25f;
        [SerializeField] private Vector2 pipeDirection = Vector2.right;

        [Header("Leaks")]
        [Tooltip("Sprite rendered where a leaking pipe cell is detected.")]
        [SerializeField] private SpriteRenderer leakSpritePrefab;
        [SerializeField] private Transform leakParent;
        [SerializeField] private float leakGrowthSpeed = 0.5f;
        [SerializeField] private float leakMaxScale = 1.2f;

        [Header("Visuals")]
        [SerializeField] private Color activeColor = new Color(0.18f, 0.58f, 0.32f);
        [SerializeField] private Color inactiveColor = new Color(0.4f, 0.4f, 0.4f);

        private readonly List<SpriteRenderer> arrowPool = new();
        private readonly List<Vector3> arrowBasePositions = new();
        private int activeArrowCount;
        private readonly Dictionary<Vector2Int, SpriteRenderer> activeLeaks = new();
        private readonly Dictionary<Vector2Int, float> leakScaleByCell = new();
        private readonly Stack<SpriteRenderer> leakPool = new();
        private bool waterActive;
        private bool isSimulationMode;
        private CanvasGroup simulationCanvasGroup;

        private void Awake()
        {
            if (simulationManager == null)
            {
                simulationManager = FindFirstObjectByType<SimulationManager>();
            }

            if (gridManager == null)
            {
                gridManager = FindFirstObjectByType<GridManager>();
            }

            if (gameModeManager == null)
            {
                gameModeManager = GameModeManager.Instance;
            }

            if (simulationPanel == null)
            {
                simulationPanel = gameObject;
            }

            simulationCanvasGroup = simulationPanel.GetComponent<CanvasGroup>();

            if (pipeArrowParent == null)
            {
                var go = new GameObject("PipeArrows");
                pipeArrowParent = go.transform;
            }

            if (leakParent == null)
            {
                var leakGo = new GameObject("Leaks");
                leakParent = leakGo.transform;
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
                simulationManager.LeaksUpdated += OnLeaksUpdated;
                waterActive = simulationManager.WaterOn;
            }

            if (gameModeManager != null)
            {
                gameModeManager.RegisterListener(this);
                isSimulationMode = gameModeManager.CurrentMode == GameMode.Simulation;
            }

            ToggleSimulationUI(isSimulationMode);
            UpdateStatus();
            RefreshPipeArrows();
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
                simulationManager.LeaksUpdated -= OnLeaksUpdated;
            }

            if (gameModeManager != null)
            {
                gameModeManager.UnregisterListener(this);
            }

            ClearArrowVisibility();
            ClearLeaks();
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

        private void OnPowerToggled(bool state)
        {
            UpdateStatus();
        }

        private void OnWaterToggled(bool state)
        {
            waterActive = state;
            UpdateStatus();
            UpdateArrowVisibility();
            UpdateLeakVisibility();
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

        private void Update()
        {
            AnimateArrows();
            AnimateLeaks();
        }

        public void OnEnterMode(GameMode newMode)
        {
            isSimulationMode = newMode == GameMode.Simulation;
            ToggleSimulationUI(isSimulationMode);
            UpdateArrowVisibility();
            UpdateLeakVisibility();
        }

        public void OnExitMode(GameMode oldMode)
        {
            if (oldMode == GameMode.Simulation)
            {
                isSimulationMode = false;
                ToggleSimulationUI(false);
                UpdateArrowVisibility();
                UpdateLeakVisibility();
            }
        }

        private void OnSimulationStepCompleted()
        {
            UpdateStatus();
            RefreshPipeArrows();
        }

        private void OnLeaksUpdated(IReadOnlyList<SimulationManager.LeakInfo> leaks)
        {
            SyncLeaks(leaks);
        }

        private void RefreshPipeArrows()
        {
            if (gridManager == null || pipeArrowPrefab == null)
            {
                ClearArrowVisibility();
                return;
            }

            activeArrowCount = 0;

            for (int y = 0; y < gridManager.height; y++)
            {
                for (int x = 0; x < gridManager.width; x++)
                {
                    var cell = gridManager.GetCell(new Vector2Int(x, y));
                    if (!cell.HasPipe) continue;

                    var renderer = GetArrowRenderer(activeArrowCount);
                    Vector3 basePos = gridManager.CellToWorld(new Vector2Int(x, y));

                    EnsureArrowBaseListSize(activeArrowCount + 1);
                    arrowBasePositions[activeArrowCount] = basePos;

                    renderer.transform.position = basePos;
                    // Pipe direction metadata is not yet tracked; default to a configurable direction for now.
                    renderer.transform.up = pipeDirection == Vector2.zero ? Vector2.right : pipeDirection.normalized;
                    renderer.enabled = ShouldShowArrows();

                    activeArrowCount++;
                }
            }

            for (int i = activeArrowCount; i < arrowPool.Count; i++)
            {
                arrowPool[i].enabled = false;
            }
        }

        private void AnimateArrows()
        {
            if (!ShouldShowArrows() || activeArrowCount == 0) return;

            Vector3 direction = (pipeDirection == Vector2.zero ? Vector2.right : pipeDirection.normalized);
            float offset = Mathf.Repeat(Time.time * arrowScrollSpeed, arrowTravelDistance);

            for (int i = 0; i < activeArrowCount; i++)
            {
                var renderer = arrowPool[i];
                if (renderer == null || !renderer.enabled) continue;

                renderer.transform.position = arrowBasePositions[i] + direction * offset;
            }
        }

        private SpriteRenderer GetArrowRenderer(int index)
        {
            while (arrowPool.Count <= index)
            {
                var instance = Instantiate(pipeArrowPrefab, pipeArrowParent);
                instance.enabled = false;
                arrowPool.Add(instance);
                arrowBasePositions.Add(Vector3.zero);
            }

            return arrowPool[index];
        }

        private void EnsureArrowBaseListSize(int desiredSize)
        {
            while (arrowBasePositions.Count < desiredSize)
            {
                arrowBasePositions.Add(Vector3.zero);
            }
        }

        private void UpdateArrowVisibility()
        {
            bool show = ShouldShowArrows();
            for (int i = 0; i < activeArrowCount; i++)
            {
                if (arrowPool[i] != null)
                {
                    arrowPool[i].enabled = show;
                }
            }
        }

        private void ClearArrowVisibility()
        {
            for (int i = 0; i < arrowPool.Count; i++)
            {
                if (arrowPool[i] != null)
                {
                    arrowPool[i].enabled = false;
                }
            }
            activeArrowCount = 0;
        }

        private bool ShouldShowArrows() => waterActive && isSimulationMode;

        private void ToggleSimulationUI(bool visible)
        {
            if (simulationPanel == null)
            {
                Debug.LogWarning("SimulationUI: No simulation panel assigned to toggle.");
                return;
            }

            if (simulationPanel == gameObject)
            {
                if (simulationCanvasGroup == null)
                {
                    simulationCanvasGroup = simulationPanel.GetComponent<CanvasGroup>();
                }

                if (simulationCanvasGroup == null)
                {
                    simulationCanvasGroup = simulationPanel.AddComponent<CanvasGroup>();
                }

                simulationCanvasGroup.alpha = visible ? 1f : 0f;
                simulationCanvasGroup.interactable = visible;
                simulationCanvasGroup.blocksRaycasts = visible;
            }
            else
            {
                if (simulationPanel.activeSelf != visible)
                {
                    simulationPanel.SetActive(visible);
                }
            }
        }

        private void SyncLeaks(IReadOnlyList<SimulationManager.LeakInfo> leaks)
        {
            if (gridManager == null || leakSpritePrefab == null)
            {
                ClearLeaks();
                return;
            }

            var desired = new HashSet<Vector2Int>();

            foreach (var leak in leaks)
            {
                desired.Add(leak.Cell);
                if (!activeLeaks.TryGetValue(leak.Cell, out var renderer) || renderer == null)
                {
                    renderer = GetLeakRenderer();
                    activeLeaks[leak.Cell] = renderer;
                    leakScaleByCell[leak.Cell] = 0f;
                }

                renderer.transform.position = leak.WorldPosition;
                renderer.transform.localScale = Vector3.zero;
                renderer.enabled = ShouldShowLeaks();
            }

            var toRemove = new List<Vector2Int>();
            foreach (var kvp in activeLeaks)
            {
                if (!desired.Contains(kvp.Key))
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var cell in toRemove)
            {
                ReleaseLeak(cell);
            }
        }

        private void AnimateLeaks()
        {
            if (!ShouldShowLeaks())
            {
                UpdateLeakVisibility();
                return;
            }

            foreach (var kvp in activeLeaks)
            {
                var cell = kvp.Key;
                var renderer = kvp.Value;
                if (renderer == null) continue;

                float currentScale = leakScaleByCell.TryGetValue(cell, out var scale) ? scale : 0f;
                currentScale = Mathf.MoveTowards(currentScale, leakMaxScale, leakGrowthSpeed * Time.deltaTime);
                leakScaleByCell[cell] = currentScale;

                renderer.transform.localScale = new Vector3(currentScale, currentScale, 1f);
                renderer.enabled = true;
            }
        }

        private SpriteRenderer GetLeakRenderer()
        {
            if (leakPool.Count > 0)
            {
                var pooled = leakPool.Pop();
                pooled.gameObject.SetActive(true);
                return pooled;
            }

            var instance = Instantiate(leakSpritePrefab, leakParent);
            instance.enabled = false;
            return instance;
        }

        private void ReleaseLeak(Vector2Int cell)
        {
            if (!activeLeaks.TryGetValue(cell, out var renderer)) return;

            renderer.enabled = false;
            renderer.transform.localScale = Vector3.zero;
            renderer.gameObject.SetActive(false);
            leakPool.Push(renderer);

            activeLeaks.Remove(cell);
            leakScaleByCell.Remove(cell);
        }

        private void ClearLeaks()
        {
            foreach (var cell in new List<Vector2Int>(activeLeaks.Keys))
            {
                ReleaseLeak(cell);
            }
        }

        private void UpdateLeakVisibility()
        {
            bool show = ShouldShowLeaks();

            foreach (var kvp in activeLeaks)
            {
                if (kvp.Value != null)
                {
                    kvp.Value.enabled = show;
                }
            }
        }

        private bool ShouldShowLeaks() => waterActive && isSimulationMode;
    }
}
