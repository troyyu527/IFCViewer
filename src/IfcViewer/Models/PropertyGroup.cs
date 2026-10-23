using System.Collections.Generic;

namespace IfcViewer.Models
{
    /// <summary>One titled block in the Properties panel, e.g. "Attributes" or "Pset_WallCommon".</summary>
    public class PropertyGroup
    {
        /// <summary>Shown for a parameter whose value differs between the selected elements.</summary>
        public const string Varies = "VARIES";

        public PropertyGroup(string title)
        {
            Title = title;
        }

        public string Title { get; }

        public List<PropertyItem> Items { get; } = new List<PropertyItem>();

        // Adds a row. A name that already exists in this group gets a number ("Framing (2)"), because a
        // multiple selection matches rows by name.

        public void Add(string name, string value)
        {

            string unique = name;
            int n = 2;

            while (Items.Exists(i => i.Name == unique))
            {
                unique = name + " (" + n + ")";
                n++;
            }

            PropertyItem newItem = new PropertyItem(unique, value);
            Items.Add(newItem);
        }
    }

    public class PropertyItem
    {
        public PropertyItem(string name, string value)
        {
            Name = name;
            Value = value ?? "";
        }

        public string Name { get; }

        public string Value { get; }
    }
}
