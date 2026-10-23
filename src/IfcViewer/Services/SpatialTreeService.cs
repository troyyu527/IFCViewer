using System;
using System.Collections.Generic;
using System.Linq;
using IfcViewer.Models;
using Xbim.Common;
using Xbim.Ifc4.Interfaces;

namespace IfcViewer.Services
{
    /// <summary>
    /// Builds the object tree from the IFC spatial structure:
    /// Project › Site › Building › Storey › contained elements › their parts.
    /// Spaces, zones and openings are left out (as in 3D); elements inside a space are shown under its storey.
    /// </summary>
    public class SpatialTreeService
    {
        // Storey elevations of the model being built, for sorting.
        private readonly Dictionary<int, double> _elevations = new Dictionary<int, double>();

        public SpatialNode Build(IModel model)
        {
            _elevations.Clear();
            foreach (var storey in model.Instances.OfType<IIfcBuildingStorey>())
            {
                if (storey.Elevation.HasValue) _elevations[storey.EntityLabel] = Convert.ToDouble(storey.Elevation.Value.Value);
            }

            var visited = new HashSet<int>();
            var project = model.Instances.FirstOrDefault<IIfcProject>();
            var root = project != null ? NodeFor(project, visited) : new SpatialNode(0, "Model");

            // Elements not placed in the spatial structure (and not parts of a placed element)
            var unassigned = model.Instances.OfType<IIfcElement>()
                .Where(e => !visited.Contains(e.EntityLabel) && !IsLeftOut(e) && !e.Decomposes.Any())
                .ToList();
            if (unassigned.Count > 0)
            {
                var group = new SpatialNode(0, "Unassigned");
                group.Children.AddRange(Sorted(unassigned.Select(e => NodeFor(e, visited))));
                root.Children.Add(group);
            }
            return root;
        }

        private SpatialNode NodeFor(IIfcObjectDefinition definition, HashSet<int> visited)
        {
            visited.Add(definition.EntityLabel);
            var node = new SpatialNode(definition.EntityLabel, DisplayName(definition), (definition as IIfcElement)?.Tag?.ToString());

            var children = new List<SpatialNode>();
            foreach (var child in ChildrenOf(definition))
            {
                if (visited.Contains(child.EntityLabel)) continue;
                if (child is IIfcOpeningElement) continue;
                if (child is IIfcSpace || child is IIfcSpatialZone)
                {
                    // Not shown, but what it contains is: lift its children one level up
                    children.AddRange(NodeFor(child, visited).Children);
                    continue;
                }
                children.Add(NodeFor(child, visited));
            }
            node.Children.AddRange(Sorted(children));
            return node;
        }

        // Aggregated parts (site → buildings → storeys, curtain wall → panels) and contained elements.
        private static IEnumerable<IIfcObjectDefinition> ChildrenOf(IIfcObjectDefinition definition)
        {
            foreach (var part in definition.IsDecomposedBy.SelectMany(rel => rel.RelatedObjects))
            {
                yield return part;
            }
            if (definition is IIfcSpatialElement spatial)
            {
                foreach (var element in spatial.ContainsElements.SelectMany(rel => rel.RelatedElements))
                {
                    yield return element;
                }
            }
        }

        private static bool IsLeftOut(IIfcElement element) => element is IIfcFeatureElementSubtraction;

        private static string DisplayName(IIfcObjectDefinition definition)
        {
            var name = definition.Name?.ToString();
            return string.IsNullOrWhiteSpace(name) ? $"{definition.ExpressType.Name} (no name)" : name;
        }

        // Storeys by elevation (bottom first), everything else by name.
        private IEnumerable<SpatialNode> Sorted(IEnumerable<SpatialNode> nodes) =>
            nodes.OrderBy(n => _elevations.TryGetValue(n.Label, out var e) ? e : double.MaxValue)
                 .ThenBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase);
    }
}
