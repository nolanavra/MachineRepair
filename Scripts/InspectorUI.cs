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

    [Header("UI Elements")]
    [SerializeField] private Text titleText;
    [SerializeField] private Text descriptionText;
    [SerializeField] private Text connectionsText;
    [SerializeField] private Text parametersText;

    private void Awake()
    {
        if (inputRouter == null) inputRouter = FindFirstObjectByType<InputRouter>();
        if (grid == null) grid = FindFirstObjectByType<GridManager>();
        if (inventory == null) inventory = FindFirstObjectByType<Inventory>();
        if (wireTool == null) wireTool = FindFirstObjectByType<WirePlacementTool>();
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
    }

    private void OnDisable()
    {
        if (inputRouter != null)
            inputRouter.SelectionChanged -= OnSelectionChanged;

        if (wireTool != null)
            wireTool.ConnectionRegistered -= OnWireConnectionRegistered;
    }

    private void OnSelectionChanged(InputRouter.SelectionInfo selection)
    {
        if (!selection.hasSelection)
        {
            ClearDisplay();
            return;
        }

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
    }

    private void PresentComponent(InputRouter.SelectionInfo selection)
    {
        var def = ResolveComponentDef(selection.cellData.component);
        string displayName = def?.displayName ?? selection.cellData.component?.name ?? "Component";

        SetTitle(displayName);
        SetDescription(def?.description);
        SetConnections(BuildConnectionSummary(selection.cell));
        SetParameters(BuildComponentParameters(def));
    }

    private void PresentPipe(InputRouter.SelectionInfo selection)
    {
        SetTitle("Pipe");
        SetDescription("Transports water between connected components.");
        SetConnections(BuildConnectionSummary(selection.cell));
        SetParameters("No simulation parameters for pipes.");
    }

    private void PresentWire(InputRouter.SelectionInfo selection)
    {
        WireType wireType = selection.cellData.wire != null ? selection.cellData.wire.wireType : WireType.None;
        string wireLabel = wireType != WireType.None ? $"{wireType} Wire" : "Wire";
        SetTitle(wireLabel);
        SetDescription("Carries electrical or signal connections between components.");
        SetConnections(BuildWireConnectionSummary(selection.cell));
        SetParameters("No simulation parameters for wires.");
    }

    private void PresentEmpty(InputRouter.SelectionInfo selection)
    {
        SetTitle($"Cell {selection.cell.x}, {selection.cell.y}");
        SetDescription("Empty cell.");
        SetConnections("No connections.");
        SetParameters(string.Empty);
    }

    private void ClearDisplay()
    {
        SetTitle("No selection");
        SetDescription(string.Empty);
        SetConnections(string.Empty);
        SetParameters(string.Empty);
    }

    private ThingDef ResolveComponentDef(MachineComponent component)
    {
        return component != null ? component.def : null;
    }

    private string BuildComponentParameters(ThingDef def)
    {
        if (def == null) return "No simulation parameters available.";

        var sb = new StringBuilder();
        sb.AppendLine("Simulation Parameters:");
        sb.AppendLine($"- Requires Power: {def.requiresPower}");
        sb.AppendLine($"- AC Passthrough: {def.passthroughPower}");
        sb.AppendLine($"- Water Passthrough: {def.passthroughWater}");
        sb.AppendLine($"- Max Pressure: {def.maxPressure} bar");
        sb.AppendLine($"- Max AC Voltage: {def.maxACVoltage}V");
        sb.AppendLine($"- Max DC Voltage: {def.maxDCVoltage}V");
        sb.AppendLine($"- Wattage: {def.wattage}W");
        sb.AppendLine($"- Flow Coefficient: {def.flowCoef}");
        sb.AppendLine($"- Volume: {def.volumeL} L");
        sb.AppendLine($"- Heat Rate: {def.heatRateW} W");
        sb.AppendLine($"- Temperature: {def.temperatureC} °C");
        sb.AppendLine($"- Target Temp Range: {def.targetTempMin} - {def.targetTempMax} °C");
        return sb.ToString();
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

    private string BuildWireConnectionSummary(Vector2Int cell)
    {
        if (wireTool == null) return BuildConnectionSummary(cell);
        if (!wireTool.TryGetConnection(cell, out var connection))
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

    private string ResolveComponentName(MachineComponent component)
    {
        var def = ResolveComponentDef(component);
        return def?.displayName ?? component?.name ?? "Component";
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
}
