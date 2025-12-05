using System.Collections.Generic;
using UnityEngine;
using MachineRepair.Grid;

namespace MachineRepair
{
    /// <summary>
    /// Lightweight overlay that renders water flow arrows over pipe cells during Simulation mode.
    /// </summary>
    public class SimulationUI : MonoBehaviour, IGameModeListener
    {
        [Header("References")]
        [SerializeField] private SimulationManager simulationManager;
        [SerializeField] private GridManager grid;
        [SerializeField] private GameModeManager gameModeManager;

        [Header("Arrow Visuals")]
        [Tooltip("Prefab with a SpriteRenderer facing +X so rotation can orient flow direction.")]
        [SerializeField] private GameObject arrowPrefab;
        [SerializeField] private Transform arrowParent;
        [SerializeField] private float arrowTravelDistance = 0.35f;
        [SerializeField] private float arrowSpeed = 1.5f;

        private readonly List<ArrowInstance> arrows = new();
        private bool waterOn;
        private bool inSimulationMode;

        private void Awake()
        {
            if (simulationManager == null) simulationManager = FindFirstObjectByType<SimulationManager>();
            if (grid == null) grid = FindFirstObjectByType<GridManager>();
            if (gameModeManager == null) gameModeManager = FindFirstObjectByType<GameModeManager>();

            waterOn = simulationManager != null && simulationManager.WaterOn;
        }

        private void OnEnable()
        {
            if (simulationManager != null)
            {
                simulationManager.SimulationStepCompleted += HandleSimulationStepCompleted;
                simulationManager.WaterToggled += HandleWaterToggled;
            }

            if (gameModeManager != null)
            {
                gameModeManager.RegisterListener(this);
            }
        }

        private void OnDisable()
        {
            if (simulationManager != null)
            {
                simulationManager.SimulationStepCompleted -= HandleSimulationStepCompleted;
                simulationManager.WaterToggled -= HandleWaterToggled;
            }

            if (gameModeManager != null)
            {
                gameModeManager.UnregisterListener(this);
            }
        }

        private void Update()
        {
            if (!waterOn || !inSimulationMode) return;

            float phase = Mathf.Repeat(Time.time * arrowSpeed, 1f) - 0.5f;
            for (int i = 0; i < arrows.Count; i++)
            {
                var arrow = arrows[i];
                if (!arrow.Active) continue;
                Vector3 offset = (Vector3)arrow.Direction * phase * arrowTravelDistance;
                arrow.Transform.position = arrow.BasePosition + offset;
            }
        }

        public void OnEnterMode(GameMode newMode)
        {
            inSimulationMode = newMode == GameMode.Simulation;
            UpdateArrowVisibility();
        }

        public void OnExitMode(GameMode oldMode)
        {
            if (oldMode == GameMode.Simulation)
            {
                inSimulationMode = false;
                UpdateArrowVisibility();
            }
        }

        private void HandleSimulationStepCompleted()
        {
            if (!inSimulationMode || simulationManager == null || grid == null) return;
            BuildArrowMap();
        }

        private void HandleWaterToggled(bool enabled)
        {
            waterOn = enabled;
            UpdateArrowVisibility();
        }

        private void BuildArrowMap()
        {
            EnsureArrowParent();

            int cursor = 0;
            for (int y = 0; y < grid.height; y++)
            {
                for (int x = 0; x < grid.width; x++)
                {
                    var cell = grid.GetCell(new Vector2Int(x, y));
                    if (!cell.HasPipe) continue;

                    var direction = ResolvePipeDirection(x, y);
                    var worldPos = grid.CellToWorld(new Vector2Int(x, y));

                    var arrow = GetOrCreateArrow(cursor++);
                    arrow.BasePosition = worldPos;
                    arrow.Direction = direction;
                    arrow.Transform.position = worldPos;
                    float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                    arrow.Transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
                    arrow.SetActive(true);
                }
            }

            // Disable any extra pooled arrows
            for (int i = cursor; i < arrows.Count; i++)
            {
                arrows[i].SetActive(false);
            }

            UpdateArrowVisibility();
        }

        private Vector2 ResolvePipeDirection(int x, int y)
        {
            // Prefer cardinal neighbor that also has a pipe to hint flow direction.
            Vector2Int[] dirs = new[]
            {
                Vector2Int.right,
                Vector2Int.up,
                Vector2Int.left,
                Vector2Int.down
            };

            foreach (var dir in dirs)
            {
                int nx = x + dir.x;
                int ny = y + dir.y;
                if (!grid.InBounds(nx, ny)) continue;
                if (grid.GetCell(new Vector2Int(nx, ny)).HasPipe)
                {
                    return dir;
                }
            }

            return Vector2.right; // fallback orientation
        }

        private ArrowInstance GetOrCreateArrow(int index)
        {
            while (arrows.Count <= index)
            {
                var go = Instantiate(arrowPrefab ?? new GameObject("WaterArrow"), arrowParent);
                if (go.GetComponent<SpriteRenderer>() == null)
                {
                    go.AddComponent<SpriteRenderer>();
                }

                arrows.Add(new ArrowInstance(go.transform));
            }

            return arrows[index];
        }

        private void UpdateArrowVisibility()
        {
            bool visible = waterOn && inSimulationMode;
            for (int i = 0; i < arrows.Count; i++)
            {
                arrows[i].SetRendererEnabled(visible && arrows[i].Active);
            }
        }

        private void EnsureArrowParent()
        {
            if (arrowParent != null) return;
            var parent = new GameObject("WaterArrows");
            parent.transform.SetParent(transform, worldPositionStays: false);
            arrowParent = parent.transform;
        }

        private class ArrowInstance
        {
            public readonly Transform Transform;
            private readonly SpriteRenderer renderer;
            public bool Active { get; private set; }
            public Vector3 BasePosition { get; set; }
            public Vector2 Direction { get; set; }

            public ArrowInstance(Transform transform)
            {
                Transform = transform;
                renderer = transform.GetComponent<SpriteRenderer>();
                Active = false;
                BasePosition = transform.position;
                Direction = Vector2.right;
            }

            public void SetActive(bool active)
            {
                Active = active;
                SetRendererEnabled(active);
            }

            public void SetRendererEnabled(bool enabled)
            {
                if (renderer != null)
                {
                    renderer.enabled = enabled;
                }
            }
        }
    }
}
