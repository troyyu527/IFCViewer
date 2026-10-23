using System.Collections.Generic;

namespace IfcViewer.Models
{
    /// <summary>
    /// One item of the object tree: a spatial element (site, building, storey), an element or a part.
    /// </summary>
    public class SpatialNode
    {
        public SpatialNode(int label, string name, string id = "")
        {
            Label = label;
            Name = name;
            Id = id ?? "";
        }

        // IFC entity label; 0 for a grouping node that is not an IFC object ("Unassigned").
        public int Label { get; }

        public string Name { get; }

        // The element's ID (IFC Tag, e.g. Revit Element ID), used by the search box; "" if none.
        public string Id { get; }

        public List<SpatialNode> Children { get; } = new List<SpatialNode>();
    }
}
