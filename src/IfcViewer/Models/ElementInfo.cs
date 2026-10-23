using System.Collections.Generic;

namespace IfcViewer.Models
{
    /// <summary>
    /// What the Properties panel shows about one element, or about several merged together.
    /// All values are already formatted as text (units converted).
    /// </summary>
    public class ElementInfo
    {
        // The element's Name, or PropertyGroup.Varies for a multiple selection with different names.
        public string Name { get; set; }

        // IFC class, e.g. "IfcWall"; "Multiple Categories" for a selection of different categories,
        // VARIES for one category with different classes (e.g. IfcStair + IfcStairFlight).
        public string IfcClass { get; set; }

        // Revit-style category, e.g. "Walls"; null for a selection of different categories.
        public string Category { get; set; }

        // Attributes, Type, property sets, quantity sets, Materials — in display order.
        public List<PropertyGroup> Groups { get; } = new List<PropertyGroup>();
    }
}
