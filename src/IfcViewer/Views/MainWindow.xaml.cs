using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using HelixToolkit.Wpf.SharpDX;
using IfcViewer.Infrastructure;
using IfcViewer.Models;
using IfcViewer.Services;
using IfcViewer.ViewModels;

namespace IfcViewer.Views
{
    /// <summary>
    /// Main window: 3D viewport between the object tree and the properties panel. Handles what needs the
    /// viewport control: clicks and hit testing, the context menu, and Fit to View.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm;
        private readonly SettingsService _settingsService = new SettingsService();
        private readonly AppSettings _settings;

        public MainWindow()
        {
            InitializeComponent();

            _settings = _settingsService.Load();
            ApplySettings();
            _vm = new MainViewModel(_settings);
            DataContext = _vm;

            // Camera moves need the viewport control, so the View performs them when the ViewModel asks
            _vm.Viewer.FitToViewRequested += (s, e) => FitToView(300);
            // Scene bounds are only known after the first frame, so fit once the window has rendered
            ContentRendered += (s, e) => Dispatcher.BeginInvoke(new System.Action(() => FitToView(0)), DispatcherPriority.Background);
            Closing += (s, e) => SaveSettings();
            Closed += (s, e) => _vm.Dispose();
            PreviewKeyDown += OnPreviewKeyDown;

            // Left click (not drag) selects; right click (not drag) opens the context menu.
            // Hit testing needs the viewport control, so it happens here.
            var leftClicks = new ClickDetector(Viewport, MouseButton.Left);
            leftClicks.Clicked += (point, modifiers) => SelectAt(point, modifiers);
            var rightClicks = new ClickDetector(Viewport, MouseButton.Right);
            rightClicks.Clicked += (point, modifiers) => ShowContextMenu(point);
        }

        // The nearest visible element under the point, or null on empty space.
        private ElementMesh ElementAt(Point point)
        {
            // Hidden elements are not hit-testable, so they are never found here
            return Viewport.FindHits(point)
                .Where(hit => (hit.ModelHit as Element3D)?.Tag is ElementMesh)
                .OrderBy(hit => hit.Distance)
                .Select(hit => (ElementMesh)((Element3D)hit.ModelHit).Tag)
                .FirstOrDefault();
        }

        // Click: select the nearest element under the point, or clear on empty space.
        // Ctrl + click: add / remove that element; on empty space nothing changes.
        private void SelectAt(Point point, ModifierKeys modifiers)
        {
            if (_vm.IsBusy) return; // the old model is being replaced
            var viewer = _vm.Viewer;
            var element = ElementAt(point);
            var ctrl = (modifiers & ModifierKeys.Control) != 0;
            if (element == null)
            {
                if (!ctrl) viewer.ClearSelection();
            }
            else if (ctrl) viewer.ToggleSelection(element.ProductLabel);
            else viewer.Select(element.ProductLabel);
        }

        // On an element: Isolate / Hide / Reset (the first two greyed out without a selection).
        // On empty space: only Reset. A right click doesn't change the selection.
        private void ShowContextMenu(Point point)
        {
            if (_vm.IsBusy) return;
            var menu = (ContextMenu)FindResource(ElementAt(point) != null ? "ElementMenu" : "BackgroundMenu");
            menu.DataContext = _vm.Viewer;
            menu.PlacementTarget = Viewport;
            menu.Placement = PlacementMode.MousePoint;
            // Open once the mouse-up is fully handled: Helix ends its pan (and releases the mouse) on the same event
            Dispatcher.BeginInvoke(new System.Action(() => menu.IsOpen = true), DispatcherPriority.Input);
        }

        // Frames the visible elements, keeping the current viewing direction.
        // Own zoom instead of Helix's: its bounding-box zoom (also used by ZoomExtentsWhenLoaded) flips the camera
        // to the opposite side with our Z-up camera, and its bounding-sphere zoom treats the field of view as
        // horizontal, but the DirectX projection uses it vertically, so a tall, narrow viewport cut the model off.
        private void FitToView(double animationMs)
        {
            // Own bounds instead of Viewport.FindBounds(), which would also count hidden elements
            var bounds = _vm.Viewer.VisibleBounds;
            if (bounds == null) return;
            var size = bounds.Value.Maximum - bounds.Value.Minimum;
            if (size.LengthSquared() == 0) return;
            if (!(Viewport.Camera is HelixToolkit.Wpf.SharpDX.PerspectiveCamera camera)) return;
            if (Viewport.ActualWidth <= 0 || Viewport.ActualHeight <= 0) return;

            // Distance at which the bounding sphere touches the narrower side of the view
            var radius = size.Length() / 2;
            var verticalFov = camera.FieldOfView * Math.PI / 180;
            var horizontalFov = 2 * Math.Atan(Math.Tan(verticalFov / 2) * Viewport.ActualWidth / Viewport.ActualHeight);
            var distance = radius / Math.Sin(Math.Min(verticalFov, horizontalFov) / 2);

            var direction = camera.LookDirection;
            direction.Normalize();
            var look = direction * distance;
            var center = bounds.Value.Center;
            camera.AnimateTo(new Point3D(center.X, center.Y, center.Z) - look, look, camera.UpDirection, animationMs);
        }

        // F: Fit to View; Esc: clear the selection. Not while typing in a text box (e.g. the search box), where the
        // keys belong to the text; read-only value boxes in the Properties panel don't count.
        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.None || _vm.IsBusy) return;
            if (Keyboard.FocusedElement is TextBox box && !box.IsReadOnly) return;

            if (e.Key == Key.F)
            {
                _vm.Viewer.FitToView();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && _vm.Viewer.HasSelection)
            {
                _vm.Viewer.ClearSelection();
                e.Handled = true;
            }
        }

        // Window position / size and panel widths from the last run. A position that is no longer on any screen
        // (e.g. a monitor was disconnected) is ignored, so the window can't open off-screen.
        private void ApplySettings()
        {
            Width = Math.Max(600, _settings.WindowWidth);
            Height = Math.Max(400, _settings.WindowHeight);
            var onScreen = !double.IsNaN(_settings.WindowLeft) && !double.IsNaN(_settings.WindowTop) &&
                           new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                                    SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight)
                               .IntersectsWith(new Rect(_settings.WindowLeft, _settings.WindowTop, Width, Height));
            if (onScreen)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = _settings.WindowLeft;
                Top = _settings.WindowTop;
            }
            if (_settings.WindowMaximized) WindowState = WindowState.Maximized;

            TreeColumn.Width = new GridLength(Math.Max(TreeColumn.MinWidth, _settings.TreeWidth));
            PropertiesColumn.Width = new GridLength(Math.Max(PropertiesColumn.MinWidth, _settings.PropertiesWidth));
        }

        private void SaveSettings()
        {
            // RestoreBounds: the normal-window position and size, also while maximized
            var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
            if (!bounds.IsEmpty)
            {
                _settings.WindowLeft = bounds.Left;
                _settings.WindowTop = bounds.Top;
                _settings.WindowWidth = bounds.Width;
                _settings.WindowHeight = bounds.Height;
            }
            _settings.WindowMaximized = WindowState == WindowState.Maximized;
            _settings.TreeWidth = TreeColumn.ActualWidth;
            _settings.PropertiesWidth = PropertiesColumn.ActualWidth;
            _settingsService.Save(_settings);
        }
    }
}
