using System.Collections.Generic;
using MachineRepair.Grid;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MachineRepair.Fluid
{
    /// <summary>
    /// Draws debug gizmos for the hydraulic network (nodes, edges, flows, pressures).
    /// Filterable by connected component to reduce clutter in complex scenes.
    /// </summary>
    [ExecuteAlways]
    public class HydraulicDebugView : MonoBehaviour
    {
        [Header("Sources")]
        [SerializeField] private SimulationManager simulationManager;
        [SerializeField] private HydraulicSystem hydraulicSystem;
        [SerializeField] private GridManager grid;

        [Header("Rendering")]
        [SerializeField] private bool drawNodes = true;
        [SerializeField] private bool drawEdges = true;
        [SerializeField] private bool drawLabels = true;
        [SerializeField] private float nodeSize = 0.15f;
        [SerializeField] private Color nodeColor = Color.cyan;
        [SerializeField] private Color fixedNodeColor = Color.magenta;
        [SerializeField] private Color edgeColor = Color.blue;
        [SerializeField] private Color positiveFlowColor = Color.green;
        [SerializeField] private Color negativeFlowColor = Color.red;

        [Header("Filters")]
        [SerializeField] private bool filterByComponent;
        [SerializeField] private int componentIndex = -1;

        private void OnDrawGizmos()
        {
            var system = ResolveSystem();
            if (system == null || grid == null)
            {
                return;
            }

            HashSet<int> allowedNodes = null;
            if (filterByComponent && componentIndex >= 0 && system.TryGetComponentNodeIndices(componentIndex, out var indices))
            {
                allowedNodes = new HashSet<int>(indices);
            }

            if (drawEdges)
            {
                DrawEdges(system, allowedNodes);
            }

            if (drawNodes)
            {
                DrawNodes(system, allowedNodes);
            }
        }

        private HydraulicSystem ResolveSystem()
        {
            if (hydraulicSystem != null)
            {
                return hydraulicSystem;
            }

            if (simulationManager != null)
            {
                hydraulicSystem = simulationManager.HydraulicSystem;
                grid ??= simulationManager.Grid;
            }

            return hydraulicSystem;
        }

        private void DrawNodes(HydraulicSystem system, HashSet<int> allowed)
        {
            var nodes = system.Nodes;
            if (nodes == null) return;

            for (int i = 0; i < nodes.Count; i++)
            {
                if (allowed != null && !allowed.Contains(i)) continue;

                var node = nodes[i];
                Vector3 world = grid.CellToWorld(node.Cell);
                Gizmos.color = node.IsFixedPressure ? fixedNodeColor : nodeColor;
                Gizmos.DrawSphere(world, nodeSize);

#if UNITY_EDITOR
                if (drawLabels)
                {
                    string label = $"[{i}] P={node.Pressure_Pa:0.###} Pa";
                    if (node.HasInjection)
                    {
                        label += $"\nQ\u25b2={node.Injection_m3s:0.###} m3/s";
                    }

                    UnityEditor.Handles.Label(world + Vector3.up * nodeSize, label);
                }
#endif
            }
        }

        private void DrawEdges(HydraulicSystem system, HashSet<int> allowed)
        {
            var edges = system.Edges;
            var nodes = system.Nodes;
            if (edges == null || nodes == null) return;

            for (int i = 0; i < edges.Count; i++)
            {
                var edge = edges[i];
                if (edge.NodeA < 0 || edge.NodeA >= nodes.Count || edge.NodeB < 0 || edge.NodeB >= nodes.Count) continue;
                if (allowed != null && (!allowed.Contains(edge.NodeA) || !allowed.Contains(edge.NodeB))) continue;

                var nodeA = nodes[edge.NodeA];
                var nodeB = nodes[edge.NodeB];
                Vector3 worldA = grid.CellToWorld(nodeA.Cell);
                Vector3 worldB = grid.CellToWorld(nodeB.Cell);

                Gizmos.color = edge.Flow_m3s >= 0 ? positiveFlowColor : negativeFlowColor;
                Gizmos.DrawLine(worldA, worldB);

#if UNITY_EDITOR
                if (drawLabels)
                {
                    Vector3 mid = (worldA + worldB) * 0.5f;
                    string label = $"Q={edge.Flow_m3s:0.###} m3/s\nΔP={edge.LastDeltaP_Pa:0.###} Pa";
                    UnityEditor.Handles.Label(mid, label);
                }
#endif
            }
        }
    }
}
