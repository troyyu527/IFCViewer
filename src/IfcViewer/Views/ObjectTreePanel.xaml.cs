using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using IfcViewer.ViewModels;

namespace IfcViewer.Views
{
    /// <summary>
    /// Object tree panel: the model's spatial hierarchy; click / Ctrl + click selects, and it scrolls to the
    /// selected item.
    /// </summary>
    public partial class ObjectTreePanel : UserControl
    {
        public ObjectTreePanel()
        {
            InitializeComponent();
            DataContextChanged += (s, e) =>
            {
                if (e.OldValue is ObjectTreeViewModel oldVm) oldVm.ScrollIntoViewRequested -= OnScrollIntoViewRequested;
                if (e.NewValue is ObjectTreeViewModel newVm) newVm.ScrollIntoViewRequested += OnScrollIntoViewRequested;
            };
        }

        // Click selects, Ctrl + click adds / removes. The TreeView's own selection is not used.
        private void OnRowMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is TreeNodeViewModel node)) return;
            if (!(DataContext is ObjectTreeViewModel vm)) return;

            vm.Click(node, (Keyboard.Modifiers & ModifierKeys.Control) != 0);
            e.Handled = true;
        }

        private void OnScrollIntoViewRequested(object sender, TreeNodeViewModel node)
        {
            // Run after the IsExpanded changes have reached the TreeViewItems
            Dispatcher.BeginInvoke(new System.Action(() => ScrollIntoView(node)), DispatcherPriority.Loaded);
        }

        // Walks from the root to the node, making sure each level's item exists (collapsed or virtualized
        // branches have no TreeViewItem until they are expanded and laid out), then brings it into view.
        private void ScrollIntoView(TreeNodeViewModel node)
        {
            var path = new List<TreeNodeViewModel>();
            for (var n = node; n != null; n = n.Parent) path.Insert(0, n);

            ItemsControl parent = Tree;
            TreeViewItem item = null;
            foreach (var step in path)
            {
                item = ContainerFor(parent, step);
                if (item == null) return;
                parent = item;
            }
            item?.BringIntoView();
        }

        private static TreeViewItem ContainerFor(ItemsControl parent, object data)
        {
            parent.UpdateLayout();
            if (parent.ItemContainerGenerator.ContainerFromItem(data) is TreeViewItem existing) return existing;

            // Virtualized: ask the panel to create the item at that index, then look again
            var index = parent.Items.IndexOf(data);
            if (index < 0) return null;
            parent.ApplyTemplate();
            var panel = FindChild<VirtualizingStackPanel>(parent);
            panel?.BringIndexIntoViewPublic(index);
            parent.UpdateLayout();
            return parent.ItemContainerGenerator.ContainerFromItem(data) as TreeViewItem;
        }

        private static T FindChild<T>(DependencyObject root) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match) return match;
                var deeper = FindChild<T>(child);
                if (deeper != null) return deeper;
            }
            return null;
        }
    }
}
