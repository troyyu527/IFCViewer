using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IfcViewer.Models;

namespace IfcViewer.ViewModels
{
    /// <summary>
    /// The object tree, linked both ways with the 3D selection: clicks in the tree change the
    /// <see cref="ViewerViewModel"/> selection, and selection / visibility changes are shown in the tree.
    /// Also searches elements by ID (search box at the top of the panel).
    /// </summary>
    public partial class ObjectTreeViewModel : ObservableObject
    {
        // Levels opened after loading: project › site › building, so the storeys are visible.
        private const int InitiallyExpandedLevels = 3;

        private readonly ViewerViewModel _viewer;
        private readonly Dictionary<int, TreeNodeViewModel> _nodes = new Dictionary<int, TreeNodeViewModel>();

        // ID → labels of the elements with that ID. Usually one; several when one source element was exported
        // as several IFC elements (mc8: Revit floor 377287 = two slabs). Dotted IDs are also listed by their
        // last part (SketchUp IFC manager: "…932084.2037920.2037925" → "2037925").
        private readonly Dictionary<string, List<int>> _labelsById = new Dictionary<string, List<int>>();

        // Text in the search box; searched on Enter, not while typing.
        [ObservableProperty] private string _searchText = "";

        // Result note under the search box, e.g. "No element with ID 12345"; empty when there's nothing to say.
        [ObservableProperty] private string _searchMessage = "";
        private readonly List<TreeNodeViewModel> _selectedNodes = new List<TreeNodeViewModel>();
        private readonly List<TreeNodeViewModel> _hiddenNodes = new List<TreeNodeViewModel>();

        public ObjectTreeViewModel(ViewerViewModel viewer)
        {
            _viewer = viewer;
            _viewer.SelectionChanged += (s, e) => ShowSelection();
            _viewer.VisibilityChanged += (s, e) => ShowHidden();
        }

        public ObservableCollection<TreeNodeViewModel> Roots { get; } = new ObservableCollection<TreeNodeViewModel>();

        // Asks the View to scroll this item into view (its parents are already expanded).
        public event EventHandler<TreeNodeViewModel> ScrollIntoViewRequested;

        // Shows the tree of a newly loaded model; null empties it (e.g. after a failed load).
        public void Load(SpatialNode root)
        {
            Roots.Clear();
            _nodes.Clear();
            _labelsById.Clear();
            _selectedNodes.Clear();
            _hiddenNodes.Clear();
            SearchText = "";
            SearchMessage = "";
            if (root == null) return;

            var top = new TreeNodeViewModel(root, null);
            Register(top, 0);
            Roots.Add(top);
            ShowSelection();
            ShowHidden();
        }

        // Click selects only this item; Ctrl + click adds or removes it.
        public void Click(TreeNodeViewModel node, bool ctrl)
        {
            if (node.Label == 0) return;
            if (ctrl) _viewer.ToggleSelection(node.Label);
            else _viewer.Select(node.Label);
        }

        // Selects every element whose ID is exactly the typed text (replacing the selection). Unknown ID: message,
        // selection unchanged. Empty text: nothing happens.
        [RelayCommand]
        private void Search()
        {
            var id = (SearchText ?? "").Trim();
            if (id.Length == 0)
            {
                SearchMessage = "";
                return;
            }
            if (!_labelsById.TryGetValue(id, out var labels))
            {
                SearchMessage = $"No element with ID {id}";
                return;
            }
            SearchMessage = labels.Count > 1 ? $"{labels.Count} elements with ID {id}" : "";
            _viewer.SelectMany(labels);
        }

        private void Register(TreeNodeViewModel node, int depth)
        {
            if (node.Label != 0)
            {
                _nodes[node.Label] = node;
                IndexId(node);
            }
            node.IsExpanded = depth < InitiallyExpandedLevels;
            foreach (var child in node.Children) Register(child, depth + 1);
        }

        private void IndexId(TreeNodeViewModel node)
        {
            var id = node.Id.Trim();
            if (id.Length == 0) return;
            AddToIndex(id, node.Label);

            var lastDot = id.LastIndexOf('.');
            if (lastDot >= 0 && lastDot < id.Length - 1) AddToIndex(id.Substring(lastDot + 1), node.Label);
        }

        private void AddToIndex(string id, int label)
        {
            if (!_labelsById.TryGetValue(id, out var labels)) _labelsById[id] = labels = new List<int>();
            if (!labels.Contains(label)) labels.Add(label);
        }

        private void ShowSelection()
        {
            foreach (var node in _selectedNodes) node.IsSelected = false;
            _selectedNodes.Clear();

            foreach (var label in _viewer.SelectedLabels)
            {
                if (!_nodes.TryGetValue(label, out var node)) continue;
                node.IsSelected = true;
                _selectedNodes.Add(node);
                for (var parent = node.Parent; parent != null; parent = parent.Parent) parent.IsExpanded = true;
            }

            // The most recently selected item (e.g. the element just clicked in 3D)
            if (_selectedNodes.Count > 0) ScrollIntoViewRequested?.Invoke(this, _selectedNodes[_selectedNodes.Count - 1]);
        }

        private void ShowHidden()
        {
            foreach (var node in _hiddenNodes) node.IsHidden = false;
            _hiddenNodes.Clear();

            foreach (var label in _viewer.HiddenLabels)
            {
                if (!_nodes.TryGetValue(label, out var node)) continue;
                node.IsHidden = true;
                _hiddenNodes.Add(node);
            }
        }
    }
}
