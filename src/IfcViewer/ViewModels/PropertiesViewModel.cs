using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using IfcViewer.Models;

namespace IfcViewer.ViewModels
{
    /// <summary>
    /// Data for the Properties panel: one element, or several merged (common parameters, VARIES).
    /// </summary>
    public partial class PropertiesViewModel : ObservableObject
    {
        // Always "Properties", as in Revit; the count is shown with the category in the header.
        public string Title => "Properties";

        // One element: its name. Several (Revit style): category and count, e.g. "Walls (3)", or "Common (5)"
        // for different categories.
        [ObservableProperty] private string _header = "No element selected";

        // IFC class; "Multiple Categories" for a selection of different categories.
        [ObservableProperty] private string _subHeader = "";

        public ObservableCollection<PropertyGroup> Groups { get; } = new ObservableCollection<PropertyGroup>();

        // info: One element's info, or the merged info of count elements.
        public void Show(ElementInfo info, int count)
        {
            if (count > 1)
            {
                Header = $"{info.Category ?? "Common"} ({count})";
            }
            else
            {
                Header = string.IsNullOrEmpty(info.Name) ? "(no name)" : info.Name;
            }
            SubHeader = info.IfcClass;

            Groups.Clear();
            foreach (var group in info.Groups) Groups.Add(group);
        }

        public void Clear()
        {
            Header = "No element selected";
            SubHeader = "";
            Groups.Clear();
        }
    }
}
