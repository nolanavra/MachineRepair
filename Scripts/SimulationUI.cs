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
    {
        [Header("References")]
        [SerializeField] private SimulationManager simulationManager;
        [SerializeField] private GridManager gridManager;
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
        [SerializeField] private float arrowTravelDistance = 3f;
        [SerializeField] private float arrowScrollSpeed = 1.25f;

        [Header("Leaks")]
        [Tooltip("Sprite rendered where a leaking pipe cell is detected.")]
        [SerializeField] private SpriteRenderer leakSpritePrefab;
        [SerializeField] private Transform leakParent;
        [SerializeField] private float leakGrowthSpeed = 0.5f;
        [SerializeField] private float leakMaxScale = 1.2f;

        [Header("Visuals")]
        [SerializeField] private Color activeColor = new Color(0.18f, 0.58f, 0.32f);
        [SerializeField] private Color inactiveColor = new Color(0.4f, 0.4f, 0.4f);

        [Header("Debugging")]
        [SerializeField] private bool logPoweredPaths = false;
        [SerializeField] private TextMeshProUGUI powerDebugLabel;
        [SerializeField] private bool logWaterArrows = false;

        private readonly List<SpriteRenderer> arrowPool = new();
        private readonly List<Vector3> arrowBasePositions = new();
        private readonly List<Vector3> arrowDirections = new();
        private readonly List<float> arrowSpeeds = new();
        private int activeArrowCount;
        private readonly Dictionary<Vector2Int, SpriteRenderer> activeLeaks = new();
        private readonly Dictionary<Vector2Int, float> leakScaleByCell = new();
        private readonly Stack<SpriteRenderer> leakPool = new();
        private bool waterActive;
        private bool simulationRunning;
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
                simulationManager.SimulationRunStateChanged += OnSimulationRunStateChanged;
                simulationManager.WaterFlowUpdated += OnWaterFlowUpdated;
                waterActive = simulationManager.WaterOn;
                simulationRunning = simulationManager.SimulationRunning;
            }

            ToggleSimulationUI(simulationRunning);
            UpdateStatus();
            ClearArrowVisibility();
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
                simulationManager.SimulationRunStateChanged -= OnSimulationRunStateChanged;
                simulationManager.WaterFlowUpdated -= OnWaterFlowUpdated;
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

        private void OnSimulationStepCompleted()
        {
            UpdateStatus();
            ShowPowerDebugInfo();
        }

        private void OnSimulationRunStateChanged(bool running)
        {
            simulationRunning = running;
            ToggleSimulationUI(simulationRunning);
            UpdateArrowVisibility();
            UpdateLeakVisibility();
        }

        private void OnLeaksUpdated(IReadOnlyList<SimulationManager.LeakInfo> leaks)
        {
            SyncLeaks(leaks);
        }

        private void OnWaterFlowUpdated(IReadOnlyList<SimulationManager.WaterFlowArrow> arrows)
        {
            if (gridManager == null || pipeArrowPrefab == null)
            {
                ClearArrowVisibility();
                return;
            }

            int desiredCount = arrows == null ? 0 : arrows.Count;
            activeArrowCount = desiredCount;

            EnsureArrowDataListSize(desiredCount);

            for (int i = 0; i < desiredCount; i++)
            {
                var arrow = arrows[i];
                var renderer = GetArrowRenderer(i);
                Vector3 direction = arrow.Direction == Vector2.zero ? Vector3.right : ((Vector3)arrow.Direction).normalized;

                arrowBasePositions[i] = arrow.Position;
                arrowDirections[i] = direction;
                arrowSpeeds[i] = Mathf.Max(0f, arrow.Speed);

                renderer.transform.position = arrow.Position;
                renderer.transform.up = direction;
                renderer.enabled = ShouldShowArrows();

                if (logWaterArrows)
                {
                    Debug.Log($"[SimulationUI] Arrow[{i}] pos={arrow.Position} dir={direction} speed={arrowSpeeds[i]:0.###} enabled={renderer.enabled}");
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

            for (int i = 0; i < activeArrowCount; i++)
            {
                var renderer = arrowPool[i];
                if (renderer == null || !renderer.enabled) continue;

                float offset = Mathf.Repeat(Time.time * arrowScrollSpeed * arrowSpeeds[i], arrowTravelDistance);
                renderer.transform.position = arrowBasePositions[i] + arrowDirections[i] * offset;
            }
        }

        private SpriteRenderer GetArrowRenderer(int index)
        {
            while (arrowPool.Count <= index)
            {
                var instance = Instantiate(pipeArrowPrefab, pipeArrowParent);
                instance.enabled = false;
                arrowPool.Add(instance);
                EnsureArrowDataListSize(arrowPool.Count);
            }

            return arrowPool[index];
        }

        private void EnsureArrowDataListSize(int desiredSize)
        {
            while (arrowBasePositions.Count < desiredSize)
            {
                arrowBasePositions.Add(Vector3.zero);
            }

            while (arrowDirections.Count < desiredSize)
            {
                arrowDirections.Add(Vector3.right);
            }

            while (arrowSpeeds.Count < desiredSize)
            {
                arrowSpeeds.Add(0f);
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

        private bool ShouldShowArrows() => waterActive && simulationRunning;

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

        private void ShowPowerDebugInfo()
        {
            if (!logPoweredPaths || simulationManager == null || gridManager == null) return;

            var snapshot = simulationManager.LastSnapshot;
            if (snapshot == null || snapshot.Value.Voltage == null)
            {
                if (powerDebugLabel != null) powerDebugLabel.text = string.Empty;
                return;
            }

            var voltage = snapshot.Value.Voltage;
            int poweredCells = 0;
            var poweredComponents = new List<string>();
            var poweredComponentNames = new HashSet<string>();

            for (int y = 0; y < gridManager.height; y++)
            {
                for (int x = 0; x < gridManager.width; x++)
                {
                    int idx = gridManager.ToIndex(new Vector2Int(x, y));
                    if (idx < 0 || idx >= voltage.Length) continue;

                    if (voltage[idx] > 0.01f)
                    {
                        poweredCells++;
                        var cell = gridManager.GetCell(new Vector2Int(x, y));
                        if (cell.component != null && cell.component.def != null)
                        {
                            if (poweredComponentNames.Add(cell.component.def.displayName))
                            {
                                poweredComponents.Add(cell.component.def.displayName);
                            }
                        }
                    }
                }
            }

            string componentList = poweredComponents.Count > 0
                ? string.Join(", ", poweredComponents)
                : "none";

            string debugText = $"Powered cells: {poweredCells}\nPowered components: {componentList}";

            if (powerDebugLabel != null)
            {
                powerDebugLabel.text = debugText;
            }

            Debug.Log(debugText);
        }

        private bool ShouldShowLeaks() => waterActive && simulationRunning;
    }
}
