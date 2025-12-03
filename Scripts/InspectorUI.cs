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
    }

    private void OnDisable()
    {
        if (inputRouter != null)
            inputRouter.SelectionChanged -= OnSelectionChanged;
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
        string wireLabel = selection.cellData.wire != WireType.None ? $"{selection.cellData.wire} Wire" : "Wire";
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

        var neighbors = new List<string>();
        var offsets = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        for (int i = 0; i < offsets.Length; i++)
        {
            var candidate = cell + offsets[i];
            if (!grid.InBounds(candidate.x, candidate.y)) continue;

            var neighborCell = grid.GetCell(candidate);
            if (!neighborCell.HasComponent) continue;

            var def = ResolveComponentDef(neighborCell.component);
            string label = def?.displayName ?? neighborCell.component.name;
            if (!neighbors.Contains(label))
                neighbors.Add(label);
        }

        return neighbors.Count switch
        {
            0 => "Not connected to any components.",
            1 => $"Connected to {neighbors[0]}",
            2 => $"Between {neighbors[0]} and {neighbors[1]}",
            _ => $"Connections: {string.Join(", ", neighbors)}"
        };
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

        if (connection.startCell == connection.endCell)
            return $"Connects {startLabel} to itself.";

        return $"Connects {startLabel} to {endLabel}.";
    }

    private string ResolveComponentName(MachineComponent component)
    {
        var def = ResolveComponentDef(component);
        return def?.displayName ?? component?.name ?? "Component";
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
