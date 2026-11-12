using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace LocalChat;

// Pixel scrolling keeps tall attachment bubbles from becoming wheel-sized steps.
public static class SmoothScrolling
{
    public const double WheelStep = 48;
    public const double DurationMilliseconds = 160;

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(SmoothScrolling), new PropertyMetadata(false, OnEnabledChanged));
    private static readonly DependencyProperty ControllerProperty = DependencyProperty.RegisterAttached(
        "Controller", typeof(Controller), typeof(SmoothScrolling));

    public static bool GetIsEnabled(DependencyObject value) => (bool)value.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject value, bool enabled) => value.SetValue(IsEnabledProperty, enabled);
    public static void Cancel(ListBox list) => (list.GetValue(ControllerProperty) as Controller)?.Stop();

    public static double CalculateTarget(double current, double previousTarget, int wheelDelta,
        double maximum, double viewport, bool active)
    {
        var distance = -wheelDelta / 120d * WheelStep;
        // Reversing the wheel takes effect immediately, rather than fighting inertia.
        var basis = active && Math.Sign(previousTarget - current) == Math.Sign(distance)
            ? previousTarget : current;
        var backlog = Math.Max(WheelStep, Math.Min(240, viewport * 0.6));
        return Math.Clamp(Math.Clamp(basis + distance, current - backlog, current + backlog), 0, maximum);
    }

    public static double Interpolate(double start, double target, double elapsedMilliseconds)
    {
        var progress = Math.Clamp(elapsedMilliseconds / DurationMilliseconds, 0, 1);
        return start + (target - start) * (1 - Math.Pow(1 - progress, 3));
    }

    private static void OnEnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not ListBox list) return;
        if (list.GetValue(ControllerProperty) is Controller previous) previous.Detach();
        list.SetValue(ControllerProperty, (bool)e.NewValue ? new Controller(list) : null);
    }

    private static T? FindChild<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindChild<T>(child) is { } descendant) return descendant;
        }
        return null;
    }

    private sealed class Controller
    {
        private readonly ListBox list;
        private readonly DispatcherTimer timer;
        private readonly Stopwatch clock = new();
        private ScrollViewer? viewer;
        private double start;
        private double target;
        private double appliedDistance;

        public Controller(ListBox list)
        {
            this.list = list;
            timer = new DispatcherTimer(DispatcherPriority.Render, list.Dispatcher)
                { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += OnTick;
            list.PreviewMouseWheel += OnWheel;
            list.PreviewMouseDown += OnMouseDown;
            list.PreviewKeyDown += OnKeyDown;
            list.Unloaded += OnUnloaded;
        }

        public void Stop()
        {
            timer.Stop();
            clock.Reset();
        }

        public void Detach()
        {
            Stop();
            timer.Tick -= OnTick;
            list.PreviewMouseWheel -= OnWheel;
            list.PreviewMouseDown -= OnMouseDown;
            list.PreviewKeyDown -= OnKeyDown;
            list.Unloaded -= OnUnloaded;
            viewer = null;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e) { Stop(); viewer = null; }
        private void OnMouseDown(object sender, MouseButtonEventArgs e) => Stop();
        private void OnKeyDown(object sender, KeyEventArgs e) => Stop();

        private void OnWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta == 0) return;
            list.ApplyTemplate();
            list.UpdateLayout();
            viewer ??= FindChild<ScrollViewer>(list);
            if (viewer == null) return;
            // Intercept at the list so read-only message TextBoxes cannot eat the wheel.
            e.Handled = true;
            var remaining = timer.IsEnabled ? target - start - appliedDistance : 0;
            start = viewer.VerticalOffset;
            target = CalculateTarget(start, start + remaining, e.Delta, viewer.ScrollableHeight,
                viewer.ViewportHeight, timer.IsEnabled);
            appliedDistance = 0;
            if (Math.Abs(target - start) < 0.1) { Stop(); return; }
            if (!SystemParameters.ClientAreaAnimation)
            {
                Stop();
                viewer.ScrollToVerticalOffset(target);
                return;
            }
            clock.Restart();
            timer.Start();
        }

        private void OnTick(object? sender, EventArgs e)
        {
            if (viewer == null) { Stop(); return; }
            // The virtualizing panel revises estimated heights as cold bubbles are
            // measured. Its absolute offset can change without the visible anchor
            // moving: apply only this frame's displacement to the latest offset.
            list.UpdateLayout();
            var elapsed = clock.Elapsed.TotalMilliseconds;
            var distance = Interpolate(0, target - start, elapsed);
            var delta = distance - appliedDistance;
            viewer.ScrollToVerticalOffset(Math.Clamp(viewer.VerticalOffset + delta, 0, viewer.ScrollableHeight));
            appliedDistance = distance;
            if (elapsed >= DurationMilliseconds) Stop();
        }
    }
}
