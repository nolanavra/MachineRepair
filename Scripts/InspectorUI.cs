using System.Collections.Generic;
using System.Text;
using MachineRepair;
using MachineRepair.Grid;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays information about the currently selected cell contents, including
/// description text and simulation parameters for components.
/// </summary>
public class InspectorUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private InputRouter inputRouter;
    [SerializeField] private GridManager grid;
    [SerializeField] private Inventory inventory;
    [SerializeField] private WirePlacementTool wireTool;
    [SerializeField] private SimulationManager simulationManager;

    [Header("UI Elements")]
    [SerializeField] private Text titleText;
    [SerializeField] private Text descriptionText;
    [SerializeField] private Text connectionsText;
    [SerializeField] private Text parametersText;
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private CanvasGroup panelCanvasGroup;
    [SerializeField] private Button removeButton;
    [Header("Switch UI")]
    [SerializeField] private GameObject switchPanel;
    [SerializeField] private Text switchStateText;
    [SerializeField] private Button switchToggleButton;

    private InputRouter.SelectionInfo currentSelection;
    private SwitchComponent inspectedSwitch;

    private void Awake()
    {
        if (inputRouter == null) inputRouter = FindFirstObjectByType<InputRouter>();
        if (grid == null) grid = FindFirstObjectByType<GridManager>();
        if (inventory == null) inventory = FindFirstObjectByType<Inventory>();
        if (wireTool == null) wireTool = FindFirstObjectByType<WirePlacementTool>();
        if (simulationManager == null) simulationManager = FindFirstObjectByType<SimulationManager>();
    }

    private void OnEnable()
    {
        if (inputRouter != null)
        {
            inputRouter.SelectionChanged += OnSelectionChanged;
            OnSelectionChanged(inputRouter.CurrentSelection);
        }
        else
        {
            ClearDisplay();
        }

        if (wireTool != null)
        {
            wireTool.ConnectionRegistered += OnWireConnectionRegistered;
        }

        if (simulationManager != null)
        {
            simulationManager.PowerToggled += OnSimulationStateChanged;
            simulationManager.WaterToggled += OnSimulationStateChanged;
            simulationManager.SimulationStepCompleted += OnSimulationStepCompleted;
        }

        if (removeButton != null)
        {
            removeButton.onClick.AddListener(OnRemoveButtonClicked);
        }

        if (switchToggleButton != null)
        {
            switchToggleButton.onClick.AddListener(OnSwitchToggleButtonClicked);
        }
    }

    private void OnDisable()
    {
        if (inputRouter != null)
            inputRouter.SelectionChanged -= OnSelectionChanged;

        if (wireTool != null)
            wireTool.ConnectionRegistered -= OnWireConnectionRegistered;

        if (simulationManager != null)
        {
            simulationManager.PowerToggled -= OnSimulationStateChanged;
            simulationManager.WaterToggled -= OnSimulationStateChanged;
            simulationManager.SimulationStepCompleted -= OnSimulationStepCompleted;
        }

        if (removeButton != null)
        {
            removeButton.onClick.RemoveListener(OnRemoveButtonClicked);
        }

        if (switchToggleButton != null)
        {
            switchToggleButton.onClick.RemoveListener(OnSwitchToggleButtonClicked);
        }
    }

    private void OnSelectionChanged(InputRouter.SelectionInfo selection)
    {
        currentSelection = selection;

        if (!selection.hasSelection)
        {
            ClearDisplay();
            return;
        }

        SetPanelVisible(true);

        switch (selection.target)
        {
            case InputRouter.CellSelectionTarget.Component:
                PresentComponent(selection);
                break;
            case InputRouter.CellSelectionTarget.Pipe:
                PresentPipe(selection);
                break;
            case InputRouter.CellSelectionTarget.Wire:
                PresentWire(selection);
                break;
            default:
                PresentEmpty(selection);
                break;
        }

        UpdateRemoveButtonState(selection);
    }

    private void OnSimulationStepCompleted()
    {
        if (currentSelection.hasSelection)
        {
            OnSelectionChanged(currentSelection);
        }
    }

    private void OnSimulationStateChanged(bool _)
    {
        if (currentSelection.hasSelection)
        {
            OnSelectionChanged(currentSelection);
        }
    }

    private void PresentComponent(InputRouter.SelectionInfo selection)
    {
        var def = ResolveComponentDef(selection.cellData.component);
        string displayName = def?.displayName ?? selection.cellData.component?.name ?? "Component";

        SetTitle(displayName);
        SetDescription(def?.description);
        SetConnections(BuildConnectionSummary(selection.cell));
        SetParameters(BuildComponentParameters(selection, def));
        PresentSwitchSection(selection.cellData.component != null
            ? selection.cellData.component.GetComponent<SwitchComponent>()
            : null);
    }

    private void PresentPipe(InputRouter.SelectionInfo selection)
    {
        SetTitle("Pipe");
        SetDescription("Transports water between connected components.");
        SetConnections(BuildConnectionSummary(selection.cell));
        SetParameters("No simulation parameters for pipes.");
        PresentSwitchSection(null);
    }

    private void PresentWire(InputRouter.SelectionInfo selection)
    {
        var wire = selection.cellData.GetWireAt(selection.wireIndex);
        WireType wireType = wire != null ? wire.wireType : WireType.None;
        string wireLabel = wireType != WireType.None ? $"{wireType} Wire" : "Wire";
        SetTitle(wireLabel);
        SetDescription("Carries electrical or signal connections between components.");
        SetConnections(BuildWireConnectionSummary(selection.cell, selection.wireIndex));
        SetParameters(BuildWireParameters(selection.cellData, selection.wireIndex));
        PresentSwitchSection(null);
    }

    private void PresentEmpty(InputRouter.SelectionInfo selection)
    {
        SetTitle($"Cell {selection.cell.x}, {selection.cell.y}");
        SetDescription("Empty cell.");
        SetConnections("No connections.");
        SetParameters(string.Empty);
        PresentSwitchSection(null);
    }

    private void ClearDisplay()
    {
        SetTitle("No selection");
        SetDescription(string.Empty);
        SetConnections(string.Empty);
        SetParameters(string.Empty);
        SetPanelVisible(false);
        PresentSwitchSection(null);
        UpdateRemoveButtonState(default);
    }

    private ThingDef ResolveComponentDef(MachineComponent component)
    {
        return component != null ? component.def : null;
    }

    private void PresentSwitchSection(SwitchComponent switchComponent)
    {
        inspectedSwitch = switchComponent;

        if (switchPanel != null)
        {
            switchPanel.SetActive(switchComponent != null);
        }

        if (switchStateText != null)
        {
            switchStateText.text = switchComponent != null
                ? $"Switch state: {(switchComponent.IsClosed ? "Closed" : "Open")}" : string.Empty;
        }

        if (switchToggleButton != null)
        {
            switchToggleButton.interactable = switchComponent != null;
        }
    }

    private string BuildComponentParameters(InputRouter.SelectionInfo selection, ThingDef def)
    {
        if (def == null) return "No simulation parameters available.";

        var sb = new StringBuilder();
        sb.AppendLine("Simulation Parameters:");

        bool powered = false;
        float flowIn = 0f;
        float pressure = 0f;

        if (grid != null && simulationManager != null && simulationManager.LastSnapshot.HasValue)
        {
            Vector2Int targetCell = selection.cellData.component != null
                ? selection.cellData.component.anchorCell
                : selection.cell;

            if (!grid.InBounds(targetCell.x, targetCell.y))
            {
                return "Simulation data unavailable (out of bounds).";
            }

            int idx = grid.ToIndex(targetCell);
            var snapshot = simulationManager.LastSnapshot.Value;

            if (snapshot.Voltage != null && idx >= 0 && idx < snapshot.Voltage.Length)
            {
                powered = simulationManager.PowerOn && snapshot.Voltage[idx] > 0.01f;
            }

            if (snapshot.Flow != null && idx >= 0 && idx < snapshot.Flow.Length)
            {
                flowIn = simulationManager.WaterOn ? snapshot.Flow[idx] : 0f;
            }

            if (snapshot.Pressure != null && idx >= 0 && idx < snapshot.Pressure.Length)
            {
                pressure = simulationManager.WaterOn ? snapshot.Pressure[idx] : 0f;
            }
        }

        sb.AppendLine($"- Powered: {powered}");
        sb.AppendLine($"- Water Flow In: {flowIn:F2}");

        if (def.type == ComponentType.Boiler)
        {
            float fillPercent = def.maxPressure > 0f ? Mathf.Clamp01(pressure / def.maxPressure) * 100f : 0f;
            float temperature = powered ? Mathf.Max(def.targetTempMin, def.temperatureC) : def.temperatureC;

            sb.AppendLine($"- Fill: {fillPercent:F0}%");
            sb.AppendLine($"- Temperature: {temperature:F1} °C");
        }

        return sb.ToString();
    }

    private void OnSwitchToggleButtonClicked()
    {
        if (inspectedSwitch == null)
            return;

        inspectedSwitch.Toggle();

        if (currentSelection.hasSelection)
        {
            OnSelectionChanged(currentSelection);
        }
    }

    private string BuildConnectionSummary(Vector2Int cell)
    {
        if (grid == null) return "Connection data unavailable.";

        if (!grid.TryGetCell(cell, out var cellData) || cellData.component == null)
            return "No connections.";

        var component = cellData.component;

        var sb = new StringBuilder();
        AppendConnectionSection(sb, "Power", component.PowerConnections);
        AppendConnectionSection(sb, "Water", component.WaterConnections);
        AppendConnectionSection(sb, "Signal", component.SignalConnections);

        return sb.Length == 0
            ? "Not connected to any components."
            : sb.ToString();
    }

    private void AppendConnectionSection(StringBuilder sb, string label, IReadOnlyList<MachineComponent> connections)
    {
        string content = connections == null || connections.Count == 0
            ? "None"
            : string.Join(", ", FormatConnectionNames(connections));

        sb.AppendLine($"{label}: {content}");
    }

    private IEnumerable<string> FormatConnectionNames(IReadOnlyList<MachineComponent> connections)
    {
        if (connections == null) yield break;

        var seen = new HashSet<MachineComponent>();
        foreach (var connection in connections)
        {
            if (connection == null || seen.Contains(connection)) continue;
            seen.Add(connection);
            yield return ResolveComponentName(connection);
        }
    }

    private string BuildWireConnectionSummary(Vector2Int cell, int wireIndex)
    {
        if (wireTool == null) return BuildConnectionSummary(cell);
        if (!wireTool.TryGetConnection(cell, wireIndex, out var connection))
            return "Wire is not connected between power ports.";

        string startName = ResolveComponentName(connection.startComponent);
        string endName = ResolveComponentName(connection.endComponent);

        string startLabel = $"{startName} at ({connection.startCell.x}, {connection.startCell.y})";
        string endLabel = $"{endName} at ({connection.endCell.x}, {connection.endCell.y})";

        string typeLabel = connection.wireType == WireType.Signal
            ? "Signal"
            : "Power";

        if (connection.startCell == connection.endCell)
            return $"{typeLabel} connection: {startLabel} loops to itself.";

        return $"{typeLabel} connection: {startLabel} to {endLabel}.";
    }

    private string BuildWireParameters(cellDef cell, int wireIndex)
    {
        var wire = cell.GetWireAt(wireIndex);
        if (wire == null || wire.wireDef == null)
            return "No simulation parameters for wires.";

        var sb = new StringBuilder();
        sb.AppendLine("Wire Parameters:");
        string kind = wire.wireDef.wireType == WireType.Signal ? "Signal" : "Power";
        sb.AppendLine($"- Kind: {kind}");
        sb.AppendLine($"- Max Current: {wire.wireDef.maxCurrent} A");

        return sb.ToString();
    }

    private string ResolveComponentName(MachineComponent component)
    {
        if (component == null)
            return "Component";

        var def = ResolveComponentDef(component);
        if (!string.IsNullOrEmpty(def?.displayName))
            return def.displayName;

        string fallbackName = component.name;
        return string.IsNullOrEmpty(fallbackName) ? "Component" : fallbackName;
    }

    private void OnWireConnectionRegistered(WirePlacementTool.WireConnectionInfo obj)
    {
        if (inputRouter != null)
        {
            OnSelectionChanged(inputRouter.CurrentSelection);
        }
    }

    private void SetTitle(string value)
    {
        if (titleText != null) titleText.text = value ?? string.Empty;
    }

    private void SetDescription(string value)
    {
        if (descriptionText != null) descriptionText.text = string.IsNullOrEmpty(value) ? string.Empty : value;
    }

    private void SetConnections(string value)
    {
        if (connectionsText != null) connectionsText.text = value ?? string.Empty;
    }

    private void SetParameters(string value)
    {
        if (parametersText != null) parametersText.text = value ?? string.Empty;
    }

    private void SetPanelVisible(bool visible)
    {
        if (panelRoot != null && panelRoot != gameObject)
        {
            if (panelRoot.activeSelf != visible)
            {
                panelRoot.SetActive(visible);
            }
            return;
        }

        if (panelCanvasGroup == null)
        {
            panelCanvasGroup = (panelRoot != null ? panelRoot.GetComponent<CanvasGroup>() : GetComponent<CanvasGroup>());
            if (panelCanvasGroup == null && !visible)
            {
                panelCanvasGroup = (panelRoot ?? gameObject).AddComponent<CanvasGroup>();
            }
        }

        if (panelCanvasGroup != null)
        {
            panelCanvasGroup.alpha = visible ? 1f : 0f;
            panelCanvasGroup.interactable = visible;
            panelCanvasGroup.blocksRaycasts = visible;
        }
    }

    private void UpdateRemoveButtonState(InputRouter.SelectionInfo selection)
    {
        if (removeButton == null)
            return;

        bool removable = SelectionIsRemovable(selection);
        removeButton.interactable = removable;
        removeButton.gameObject.SetActive(removable || panelRoot == null || panelRoot.activeSelf);
    }

    private bool SelectionIsRemovable(InputRouter.SelectionInfo selection)
    {
        if (!selection.hasSelection)
            return false;

        switch (selection.target)
        {
            case InputRouter.CellSelectionTarget.Component:
                return selection.cellData.component != null;
            case InputRouter.CellSelectionTarget.Wire:
                return selection.wireIndex >= 0 && selection.cellData.GetWireAt(selection.wireIndex) != null;
            case InputRouter.CellSelectionTarget.Pipe:
                return selection.pipeIndex >= 0 && selection.cellData.GetPipeAt(selection.pipeIndex) != null;
            default:
                return false;
        }
    }

    private void OnRemoveButtonClicked()
    {
        if (grid == null || !SelectionIsRemovable(currentSelection))
            return;

        if (grid.TryDeleteSelection(currentSelection))
        {
            if (inputRouter != null)
            {
                inputRouter.ClearSelection();
            }
            else
            {
                OnSelectionChanged(default);
            }
        }
    }
}
