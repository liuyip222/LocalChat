using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LocalChat;

internal static class SmoothScrollingTests
{
    internal static void Run(Action<bool, string> check, ListBox list)
    {
        check(VirtualizingPanel.GetScrollUnit(list) == ScrollUnit.Pixel
            && VirtualizingPanel.GetIsVirtualizing(list)
            && VirtualizingPanel.GetVirtualizationMode(list) == VirtualizationMode.Recycling
            && ScrollViewer.GetCanContentScroll(list) && SmoothScrolling.GetIsEnabled(list),
            "chat uses smooth pixel scrolling without disabling recycling virtualization");
        check(SmoothScrolling.CalculateTarget(300, 300, -120, 2000, 400, false) == 348,
            "one wheel notch moves 48 DIPs instead of whole message bubbles");
        check(SmoothScrolling.CalculateTarget(300, 300, -30, 2000, 400, false) == 312,
            "high-resolution wheel deltas retain proportional small steps");
        check(SmoothScrolling.CalculateTarget(300, 348, -120, 2000, 400, true) == 396,
            "successive wheel events accumulate their destinations");
        check(SmoothScrolling.CalculateTarget(300, 396, 120, 2000, 400, true) == 252,
            "wheel reversal immediately changes direction from the current position");
        check(SmoothScrolling.CalculateTarget(300, 540, -120, 2000, 400, true) == 540,
            "rapid wheel input has a bounded animation backlog");
        check(SmoothScrolling.CalculateTarget(10, 10, 120, 2000, 400, false) == 0
            && SmoothScrolling.CalculateTarget(1990, 1990, -120, 2000, 400, false) == 2000,
            "wheel destinations clamp at both ends of the chat");
        check(SmoothScrolling.Interpolate(300, 348, 0) == 300
            && SmoothScrolling.Interpolate(300, 348, 80) is > 300 and < 348
            && SmoothScrolling.Interpolate(300, 348, 160) == 348,
            "scroll animation starts at the current position and settles within 160ms");

        // Real WPF layout and routed wheel events, using only synthetic messages.
        VirtualizingPanel.SetCacheLength(list, new VirtualizationCacheLength(0));
        list.ItemsSource = Enumerable.Range(0, 200).Select(i => new Message
        {
            Text = i == 199 ? string.Join('\n', Enumerable.Repeat("最新消息", 16))
                : i == 198 ? string.Join('\n', Enumerable.Repeat("上一个很长的气泡", 80))
                : i % 3 == 0 ? string.Join('\n', Enumerable.Repeat("长气泡滚动测试", 12)) : "短气泡 " + i,
            Attachments = i % 7 == 0 ? [new Attachment
            {
                Kind = "image", OriginalName = "测试图片.png",
                Thumbnail = BitmapSource.Create(100, 260, 96, 96, PixelFormats.Bgra32, null,
                    new byte[100 * 260 * 4], 100 * 4)
            }] : [],
            DateVisibility = Visibility.Collapsed
        }).ToArray();
        list.ScrollIntoView(list.Items[list.Items.Count - 1]);
        list.Measure(new Size(700, 360));
        list.Arrange(new Rect(0, 0, 700, 360));
        list.UpdateLayout();
        Pump(60);
        var viewer = Find<ScrollViewer>(list)!;
        check(viewer.ScrollableHeight > 2000 && viewer.ViewportHeight <= 360,
            "mixed-height real chat bubbles expose pixel-based scroll extents");
        var last = (FrameworkElement)list.ItemContainerGenerator.ContainerFromIndex(list.Items.Count - 1);
        var lastY = last.TranslatePoint(new Point(), list).Y;
        var startupOffset = viewer.VerticalOffset;
        var startupExtent = viewer.ExtentHeight;
        list.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120)
            { RoutedEvent = Mouse.PreviewMouseWheelEvent });
        // Realizing additional cold items changes the estimated extent, not the visible anchor.
        Pump(30);
        VirtualizingPanel.SetCacheLength(list, new VirtualizationCacheLength(2));
        Pump(240);
        var movedY = last.TranslatePoint(new Point(), list).Y - lastY;
        check(Math.Abs(movedY - 48) < 1,
            $"first upward wheel after startup moves the visible bubble only 48 DIPs (bubble={movedY:0.##}, offset={startupOffset:0.##}->{viewer.VerticalOffset:0.##}, extent={startupExtent:0.##}->{viewer.ExtentHeight:0.##})");
        var settledY = last.TranslatePoint(new Point(), list).Y;
        list.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120)
            { RoutedEvent = Mouse.PreviewMouseWheelEvent });
        Pump(40);
        list.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120)
            { RoutedEvent = Mouse.PreviewMouseWheelEvent });
        Pump(240);
        check(Math.Abs(last.TranslatePoint(new Point(), list).Y - settledY - 96) < 1,
            "retargeted animation preserves the remaining wheel distance after cold layout stabilization");
        viewer.ScrollToVerticalOffset(300);
        Pump(60);
        var initial = viewer.VerticalOffset;
        var text = Find<TextBox>(list)!;
        var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
            { RoutedEvent = Mouse.PreviewMouseWheelEvent };
        text.RaiseEvent(wheel);
        check(wheel.Handled, "wheel over selectable bubble text is handled by the chat list");
        Pump(50);
        check(!SystemParameters.ClientAreaAnimation || viewer.VerticalOffset > initial
            && viewer.VerticalOffset < initial + 48,
            "real chat wheel animates through intermediate positions rather than jumping");
        Pump(190);
        check(Math.Abs(viewer.VerticalOffset - initial - 48) < 1,
            $"real chat wheel settles at one small pixel step (from {initial:0.##} to {viewer.VerticalOffset:0.##})");
        var panel = Find<VirtualizingStackPanel>(list)!;
        check(panel.Children.Count < 200, "mixed-height chat keeps offscreen messages virtualized while scrolling");

        list.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
            { RoutedEvent = Mouse.PreviewMouseWheelEvent });
        SmoothScrolling.Cancel(list);
        viewer.ScrollToVerticalOffset(600);
        Pump(60);
        var navigationOffset = viewer.VerticalOffset;
        Pump(240);
        check(Math.Abs(viewer.VerticalOffset - navigationOffset) < 1,
            "programmatic navigation cancels wheel animation instead of drifting back");
        list.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
            { RoutedEvent = Mouse.PreviewMouseWheelEvent });
        list.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            { RoutedEvent = Mouse.PreviewMouseDownEvent });
        viewer.ScrollToVerticalOffset(900);
        Pump(60);
        var dragOffset = viewer.VerticalOffset;
        Pump(240);
        check(Math.Abs(viewer.VerticalOffset - dragOffset) < 1,
            "mouse interaction stops pending animation for direct scrollbar control");
        SmoothScrolling.Cancel(list);
        viewer.ScrollToVerticalOffset(0);
        Pump(60);
        list.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120)
            { RoutedEvent = Mouse.PreviewMouseWheelEvent });
        Pump(200);
        check(viewer.VerticalOffset == 0, "wheel at the top does not overscroll or jump to another bubble");
    }

    private static T? Find<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            if (Find<T>(child) is { } nested) return nested;
        }
        return null;
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
            { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
