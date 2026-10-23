using System;
using System.Windows;
using System.Windows.Input;

namespace IfcViewer.Infrastructure
{
    /// <summary>
    /// Tells a click of one mouse button apart from a drag with it. Both buttons are shared: left-drag rotates
    /// the view and a left click selects; right-drag pans and a right click opens the context menu.
    /// Uses Preview events because they still fire while Helix handles the rotation or pan.
    /// </summary>
    public class ClickDetector
    {
        /// If the mouse moves less than 4 pixels between press and release, it counts as a click. With more movement it was a drag (rotation).
        private const double Threshold = 4.0;

        private readonly UIElement _target;
        private readonly MouseButton _button;
        private Point _down;
        private bool _isDown;

        // Raised with the click position (relative to the target) and the modifier keys held (e.g. Ctrl).
        public event Action<Point, ModifierKeys> Clicked;

        public ClickDetector(UIElement target, MouseButton button = MouseButton.Left)
        {
            _target = target;
            _button = button;
            target.PreviewMouseDown += OnDown;
            target.PreviewMouseUp += OnUp;
        }

        private void OnDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != _button) return;
            _down = e.GetPosition(_target);
            _isDown = true;
        }

        private void OnUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != _button) return;

            // Ignore a release whose press happened outside the target
            if (!_isDown) return;
            _isDown = false;

            var up = e.GetPosition(_target);
            if ((up - _down).Length < Threshold) Clicked?.Invoke(up, Keyboard.Modifiers);
        }
    }
}
