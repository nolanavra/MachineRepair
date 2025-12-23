using System.Collections.Generic;
using System.Text;
using MachineRepair;
using MachineRepair.Fluid;
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
        var pipe = selection.cellData.GetPipeAt(selection.pipeIndex);
        string displayName = pipe?.pipeDef?.displayName ?? pipe?.name ?? "Pipe";

        SetTitle(displayName);
        SetDescription("Transports water between connected components.");
        SetConnections(BuildPipeConnectionSummary(pipe));
        SetParameters(BuildPipeParameters(selection, pipe));
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

        var component = selection.cellData.component;
        bool hasSnapshot = simulationManager != null && simulationManager.LastSnapshot.HasValue;
        SimulationManager.SimulationSnapshot snapshot = hasSnapshot
            ? simulationManager.LastSnapshot.Value
            : default;

        bool powered = false;
        float flowIn = 0f;
        float fillPercent = 0f;

        Vector2Int targetCell = component != null ? component.anchorCell : selection.cell;
        bool cellValid = grid != null && grid.InBounds(targetCell.x, targetCell.y);
        int idx = cellValid && grid != null ? grid.ToIndex(targetCell) : -1;

        if (cellValid && hasSnapshot)
        {
            if (snapshot.Voltage != null && idx >= 0 && idx < snapshot.Voltage.Length)
            {
                powered = simulationManager.PowerOn && snapshot.Voltage[idx] > 0.01f;
            }

            if (snapshot.Flow != null && idx >= 0 && idx < snapshot.Flow.Length)
            {
                flowIn = simulationManager.WaterOn ? snapshot.Flow[idx] : 0f;
            }
        }
        else if (!cellValid)
        {
            sb.AppendLine("- Simulation data unavailable (out of bounds).");
        }
        else if (!hasSnapshot)
        {
            sb.AppendLine("- Simulation snapshot unavailable.");
        }

        if (def.water)
        {
            fillPercent = ResolveComponentFillPercent(component, def, snapshot, hasSnapshot);
        }

        sb.AppendLine($"- Powered: {powered}");
        sb.AppendLine($"- Water Flow In: {flowIn:F2}");

        if (def.water)
        {
            sb.AppendLine($"- Fill: {fillPercent:F1}%");
        }

        if (def.componentType == ComponentType.Boiler)
        {
            float temperature = powered ? Mathf.Max(def.targetTempMin, def.temperatureC) : def.temperatureC;

            sb.AppendLine($"- Temperature: {temperature:F1} °C");
        }

        AppendWaterPortHydraulicStates(sb, component, snapshot, hasSnapshot);

        return sb.ToString();
    }

    private float ResolveComponentFillPercent(
        MachineComponent component,
        ThingDef def,
        SimulationManager.SimulationSnapshot snapshot,
        bool hasSnapshot)
    {
        if (component != null && hasSnapshot)
        {
            if (snapshot.TryGetComponentFill(component.GetInstanceID(), out var fillFromId))
            {
                return fillFromId;
            }

            if (snapshot.TryGetComponentFill(component.anchorCell, out var fillFromAnchor))
            {
                return fillFromAnchor;
            }
        }

        if (component != null)
        {
            return component.WaterFillPercent;
        }

        return def != null ? MachineComponent.NormalizeFill01(def.fillLevel) * 100f : 0f;
    }

    private static void AppendWaterPortHydraulicStates(
        StringBuilder sb,
        MachineComponent component,
        SimulationManager.SimulationSnapshot snapshot,
        bool hasSnapshot)
    {
        if (component?.portDef?.ports == null || component.portDef.ports.Length == 0)
        {
            return;
        }

        var ports = component.portDef.ports;
        bool printedHeader = false;

        for (int i = 0; i < ports.Length; i++)
        {
            var port = ports[i];
            if (port.portType != PortType.Water)
            {
                continue;
            }

            if (!printedHeader)
            {
                sb.AppendLine("Water Ports:");
                printedHeader = true;
            }

            Vector2Int globalPortCell = component.GetGlobalCell(port);
            string linePrefix = $"- Port {i} @ ({globalPortCell.x},{globalPortCell.y}): ";

            if (hasSnapshot && snapshot.TryGetPortHydraulicState(globalPortCell, out HydraulicSystem.PortHydraulicState state))
            {
                bool hasDeltaP = snapshot.TryGetPipeDeltaP(globalPortCell, out var deltaP);
                string sourceLabel = state.IsSource ? "Source" : "Node";
                string sourcePressure = state.IsSource && state.SourcePressure_Pa > 0f
                    ? $" (Set {state.SourcePressure_Pa:0.###} Pa)"
                    : string.Empty;
                string deltaLabel = hasDeltaP ? $", ΔP {deltaP:0.###} Pa" : string.Empty;

                sb.AppendLine($"{linePrefix}[{sourceLabel}{sourcePressure}] Pressure {state.Pressure_Pa:0.###} Pa, Flow {state.Flow_m3s:0.###} m³/s{deltaLabel}");
            }
            else if (!hasSnapshot)
            {
                sb.AppendLine($"{linePrefix}Hydraulic data unavailable (no snapshot).");
            }
            else
            {
                sb.AppendLine($"{linePrefix}Hydraulic data unavailable.");
            }
        }
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

    private string BuildPipeConnectionSummary(PlacedPipe pipe)
    {
        if (pipe == null)
            return "Pipe data unavailable.";

        var sb = new StringBuilder();
        sb.AppendLine("Connections:");

        string startName = ResolveComponentName(pipe.startComponent, "(missing)");
        string endName = ResolveComponentName(pipe.endComponent, "(missing)");

        sb.AppendLine($"- Start: {startName} @ {FormatCell(pipe.startPortCell)}");
        sb.AppendLine($"- End: {endName} @ {FormatCell(pipe.endPortCell)}");

        return sb.ToString();
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

    private string BuildPipeParameters(InputRouter.SelectionInfo selection, PlacedPipe pipe)
    {
        if (pipe == null)
        {
            return "No simulation parameters for pipes.";
        }

        var sb = new StringBuilder();
        sb.AppendLine("Pipe Parameters:");

        var def = pipe.pipeDef;
        if (def != null)
        {
            sb.AppendLine($"- Max Flow: {def.maxFlow:0.###} m³/s");
            sb.AppendLine($"- Max Pressure: {def.maxPressure:0.###} Pa");
            sb.AppendLine($"- Inner Diameter: {def.innerDiameter_m:0.###} m");
        }

        int runCells = pipe.occupiedCells != null ? pipe.occupiedCells.Count : 0;
        if (runCells > 0)
        {
            sb.AppendLine($"- Run Length: {runCells} cell{(runCells == 1 ? string.Empty : "s")}");
        }

        bool hasSnapshot = simulationManager != null && simulationManager.LastSnapshot.HasValue;
        if (!hasSnapshot)
        {
            sb.AppendLine("- Runtime data unavailable (no simulation snapshot).");
            return sb.ToString();
        }

        var snapshot = simulationManager.LastSnapshot.Value;
        if (!snapshot.TryGetPipeRuntimeState(pipe.GetInstanceID(), out var runtimeState))
        {
            sb.AppendLine("- Runtime data unavailable (pipe not present in snapshot).");
            return sb.ToString();
        }

        sb.AppendLine($"- Flow: {runtimeState.Flow_m3s:0.###} m³/s");
        sb.AppendLine($"- Fill: {runtimeState.FillPercent:0.###}%");
        sb.AppendLine($"- Inlet Pressure: {runtimeState.InletPressure_Pa:0.###} Pa");
        sb.AppendLine($"- Outlet Pressure: {runtimeState.OutletPressure_Pa:0.###} Pa");
        sb.AppendLine($"- Volume: {runtimeState.Volume_m3:0.###} m³");

        Vector2Int? sampleCell = null;
        if (pipe.occupiedCells != null && pipe.occupiedCells.Count > 0)
        {
            sampleCell = pipe.occupiedCells[0];
        }
        else if (selection.hasSelection)
        {
            sampleCell = selection.cell;
        }

        if (sampleCell.HasValue && snapshot.TryGetPipeDeltaP(sampleCell.Value, out var deltaP))
        {
            sb.AppendLine($"- Pressure Drop: {deltaP:0.###} Pa");
        }

        return sb.ToString();
    }

    private string ResolveComponentName(MachineComponent component, string missingLabel = "Component")
    {
        if (component == null)
            return missingLabel;

        var def = ResolveComponentDef(component);
        if (!string.IsNullOrEmpty(def?.displayName))
            return def.displayName;

        string fallbackName = component.name;
        return string.IsNullOrEmpty(fallbackName) ? "Component" : fallbackName;
    }

    private static string FormatCell(Vector2Int cell)
    {
        return $"({cell.x}, {cell.y})";
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

    public void ToggleInspector(bool? visible = null)
    {
        bool targetVisible = visible ?? !IsPanelVisible();
        SetPanelVisible(targetVisible);
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

    private bool IsPanelVisible()
    {
        if (panelRoot != null && panelRoot != gameObject)
        {
            return panelRoot.activeSelf;
        }

        if (panelCanvasGroup != null)
        {
            return panelCanvasGroup.alpha > 0f && panelCanvasGroup.interactable;
        }

        return isActiveAndEnabled;
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
