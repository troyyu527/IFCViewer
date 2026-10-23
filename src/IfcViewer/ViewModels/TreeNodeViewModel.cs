using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using IfcViewer.Models;

namespace IfcViewer.ViewModels
{
    /// <summary>
    /// One item in the object tree.
    /// </summary>
    public partial class TreeNodeViewModel : ObservableObject
    {
        public TreeNodeViewModel(SpatialNode node, TreeNodeViewModel parent)
        {
            Label = node.Label;
            Name = node.Name;
            Id = node.Id;
            Parent = parent;
            Children = node.Children.Select(child => new TreeNodeViewModel(child, this)).ToList();
        }

        // IFC entity label; 0 for a grouping node ("Unassigned"), which can't be selected.
        public int Label { get; }

        public string Name { get; }

        // The element's ID (IFC Tag); "" if none.
        public string Id { get; }

        public TreeNodeViewModel Parent { get; }

        public IReadOnlyList<TreeNodeViewModel> Children { get; }

        [ObservableProperty] private bool _isExpanded;

        // Part of the selection (own flag: the WPF TreeView only knows single selection).
        [ObservableProperty] private bool _isSelected;

        // Hidden in 3D (Isolate / Hide): shown greyed.
        [ObservableProperty] private bool _isHidden;
    }
}
