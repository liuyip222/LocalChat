using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using MediaColor = System.Windows.Media.Color;

namespace LocalChat;

// Icon-only command row and the tool-specific parameter strip shown beneath it.
public sealed class ScreenshotToolbar
{
    private static readonly Color Accent = MediaColor.FromRgb(0, 190, 112);
    private static readonly Color[] Palette = [MediaColor.FromRgb(8, 174, 241), MediaColor.FromRgb(153, 213, 0), MediaColor.FromRgb(255, 196, 0), MediaColor.FromRgb(75, 75, 75), Colors.White, MediaColor.FromRgb(255, 76, 84)];
    private static readonly string[] ColorNames = ["蓝色", "绿色", "黄色", "深灰", "白色", "红色"];
    private static readonly Style ButtonStyle = CreateButtonStyle();
    private readonly List<Button> buttons = [];
    private readonly Dictionary<ScreenshotTool, int> sizes = new()
    {
        [ScreenshotTool.Rectangle] = 1,
        [ScreenshotTool.Ellipse] = 1,
        [ScreenshotTool.Arrow] = 1,
        [ScreenshotTool.Text] = 1,
        [ScreenshotTool.Mosaic] = 1
    };
    private readonly HashSet<ScreenshotTool> filled = [];
    public Border MainBar { get; }
    public Border OptionsBar { get; }
    public IReadOnlyList<Button> Buttons => buttons;
    public ScreenshotTool SelectedTool { get; private set; } = ScreenshotTool.Selection;
    public Color Color { get; private set; } = Palette[^1];
    public double Thickness => new double[] { 2, 4, 8 }[sizes.GetValueOrDefault(SelectedTool, 1)];
    public double FontSize => new double[] { 16, 24, 36 }[sizes[ScreenshotTool.Text]];
    public int MosaicSize => new[] { 6, 12, 24 }[sizes[ScreenshotTool.Mosaic]];
    public bool Fill => filled.Contains(SelectedTool);
    public event Action<ScreenshotTool>? ToolChanged;
    public event Action? StyleChanged;

    public ScreenshotToolbar(Action undo, Action redo, Action cancel, Action finish)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (tool, name) in new[]
        {
            (ScreenshotTool.Rectangle, "方框"), (ScreenshotTool.Ellipse, "圆框"), (ScreenshotTool.Arrow, "箭头"),
            (ScreenshotTool.Text, "文字"), (ScreenshotTool.Mosaic, "马赛克")
        })
        {
            var button = IconButton(name, Icon(tool.ToString(), Brushes.DimGray), () =>
            {
                SetTool(SelectedTool == tool ? ScreenshotTool.Selection : tool);
                ToolChanged?.Invoke(SelectedTool);
            });
            button.ToolTip = name + "（再次点击收起参数并调整选区）";
            row.Children.Add(button); buttons.Add(button);
        }
        foreach (var (name, icon, action) in new[] { ("撤销", "Undo", undo), ("重做", "Redo", redo), ("取消", "Cancel", cancel), ("完成", "Done", finish) })
        {
            var brush = icon == "Cancel" ? Brushes.Crimson : icon == "Done" ? new SolidColorBrush(Accent) : Brushes.DimGray;
            var button = IconButton(name, Icon(icon, brush), action);
            row.Children.Add(button); buttons.Add(button);
        }
        MainBar = Bar(row);
        OptionsBar = Bar(new StackPanel());
        MainBar.Visibility = OptionsBar.Visibility = Visibility.Collapsed;
    }

    public void SetTool(ScreenshotTool tool)
    {
        SelectedTool = tool;
        for (var i = 0; i < 5; i++)
        {
            var active = (ScreenshotTool)(i + 1) == tool;
            buttons[i].Background = active ? new SolidColorBrush(MediaColor.FromRgb(234, 236, 234)) : Brushes.Transparent;
            buttons[i].Content = Icon(((ScreenshotTool)(i + 1)).ToString(), active ? new SolidColorBrush(Accent) : Brushes.DimGray);
        }
        RebuildOptions();
    }

    public void UpdateHistory(bool undo, bool redo) { buttons[5].IsEnabled = undo; buttons[6].IsEnabled = redo; }

    private void RebuildOptions()
    {
        if (SelectedTool == ScreenshotTool.Selection) { OptionsBar.Visibility = Visibility.Collapsed; return; }
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < 3; i++)
        {
            var index = i;
            var active = !Fill && sizes[SelectedTool] == i;
            FrameworkElement mark = SelectedTool == ScreenshotTool.Text
                ? new TextBlock { Text = "T", FontFamily = new FontFamily("Segoe UI"), FontSize = 12 + i * 4, Foreground = active ? new SolidColorBrush(Accent) : Brushes.DarkGray, VerticalAlignment = VerticalAlignment.Center }
                : new Ellipse { Width = 4 + i * 4, Height = 4 + i * 4, Fill = active ? new SolidColorBrush(Accent) : Brushes.DarkGray };
            var name = new[] { "小", "中", "大" }[i];
            var button = OptionButton(name, mark, () => { sizes[SelectedTool] = index; filled.Remove(SelectedTool); Changed(); });
            row.Children.Add(button);
        }
        if (SelectedTool is ScreenshotTool.Rectangle or ScreenshotTool.Ellipse)
        {
            var fillMark = new Canvas { Width = 20, Height = 20 };
            var fillBrush = Fill ? new SolidColorBrush(Accent) : Brushes.DarkGray;
            Shape outer = SelectedTool == ScreenshotTool.Rectangle ? new Rectangle() : new Ellipse();
            outer.Width = outer.Height = 18; outer.Stroke = fillBrush; outer.StrokeThickness = 1.5;
            Canvas.SetLeft(outer, 1); Canvas.SetTop(outer, 1); fillMark.Children.Add(outer);
            Shape inner = SelectedTool == ScreenshotTool.Rectangle ? new Rectangle() : new Ellipse();
            inner.Width = inner.Height = 12; inner.Fill = fillBrush;
            Canvas.SetLeft(inner, 4); Canvas.SetTop(inner, 4); fillMark.Children.Add(inner);
            row.Children.Add(OptionButton("填充", fillMark, () => { filled.Add(SelectedTool); Changed(); }));
        }
        if (SelectedTool != ScreenshotTool.Mosaic)
        {
            row.Children.Add(new Border { Width = 1, Height = 20, Margin = new Thickness(7, 0, 7, 0), Background = Brushes.LightGray });
            for (var i = 0; i < Palette.Length; i++)
            {
                var color = Palette[i];
                var swatch = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(1), Background = new SolidColorBrush(color), BorderBrush = color == Colors.White ? Brushes.LightGray : Brushes.Transparent, BorderThickness = new Thickness(1) };
                var button = OptionButton(ColorNames[i], swatch, () => { Color = color; Changed(); });
                if (Color == color) button.Background = new SolidColorBrush(MediaColor.FromRgb(225, 226, 225));
                row.Children.Add(button);
            }
        }
        ((Viewbox)OptionsBar.Child).Child = row;
        OptionsBar.Visibility = Visibility.Visible;
    }
    private void Changed() { RebuildOptions(); StyleChanged?.Invoke(); }

    private static Border Bar(UIElement child) => new()
    {
        Background = new SolidColorBrush(MediaColor.FromRgb(250, 250, 250)),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(8, 5, 8, 5),
        Child = new Viewbox { Child = child, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly }
    };
    private static Button IconButton(string name, FrameworkElement icon, Action action)
    {
        var button = new Button { Content = icon, Style = ButtonStyle, Width = 38, Height = 38, Padding = new Thickness(6), Margin = new Thickness(3, 0, 3, 0), ToolTip = name, Background = Brushes.Transparent };
        System.Windows.Automation.AutomationProperties.SetName(button, name);
        button.Click += (_, _) => action();
        return button;
    }
    private static Button OptionButton(string name, FrameworkElement icon, Action action)
    {
        var button = IconButton(name, icon, action);
        button.Width = 28; button.Height = 28; button.Padding = new Thickness(3); button.Margin = new Thickness(2, 0, 2, 0);
        return button;
    }
    private static Style CreateButtonStyle()
    {
        var frame = new FrameworkElementFactory(typeof(Border));
        frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        frame.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        frame.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        frame.AppendChild(content);
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(Button)) { VisualTree = frame }));
        style.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.3));
        style.Triggers.Add(disabled);
        return style;
    }

    private static FrameworkElement Icon(string name, Brush stroke)
    {
        var canvas = new Canvas { Width = 28, Height = 28, IsHitTestVisible = false };
        if (name == "Ellipse") canvas.Children.Add(new Ellipse { Width = 22, Height = 22, Margin = new Thickness(3), Stroke = stroke, StrokeThickness = 1.8 });
        else if (name == "Mosaic")
        {
            canvas.Children.Add(new Rectangle { Width = 22, Height = 22, Margin = new Thickness(3), Stroke = stroke, StrokeThickness = 1.8 });
            for (var y = 0; y < 4; y++)
                for (var x = 0; x < 4; x++)
                    if ((x + y) % 2 == 0)
                    {
                        var tile = new Rectangle { Width = 4, Height = 4, Fill = stroke };
                        Canvas.SetLeft(tile, 6 + x * 4); Canvas.SetTop(tile, 6 + y * 4); canvas.Children.Add(tile);
                    }
        }
        else
        {
            var data = name switch
            {
                "Rectangle" => "M4,5 H24 V23 H4 Z",
                "Arrow" => "M5,23 L23,5 M10,5 H23 V18",
                "Text" => "M5,5 H23 M14,5 V24",
                "Undo" or "Redo" => "M10,5 L4,11 L10,17 M5,11 H17 C28,11 28,24 17,24 H9",
                "Cancel" => "M6,6 L22,22 M22,6 L6,22",
                _ => "M4,15 L11,22 L25,6"
            };
            canvas.Children.Add(new Path { Data = Geometry.Parse(data), Stroke = stroke, StrokeThickness = 1.8, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round });
            if (name == "Redo") canvas.RenderTransform = new ScaleTransform(-1, 1, 14, 14);
        }
        return new Viewbox { Width = 24, Height = 24, Child = canvas };
    }
}
