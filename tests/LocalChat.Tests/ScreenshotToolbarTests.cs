using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalChat;

internal static class ScreenshotToolbarTests
{
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static Button[] Options(ScreenshotToolbar toolbar) => ((StackPanel)((Viewbox)toolbar.OptionsBar.Child).Child).Children.OfType<Button>().ToArray();

    internal static void Run(Action<bool, string> check)
    {
        var selected = ScreenshotTool.Selection;
        var toolbar = new ScreenshotToolbar(() => { }, () => { }, () => { }, () => { });
        toolbar.ToolChanged += tool => selected = tool;
        check(toolbar.Buttons.Select(AutomationProperties.GetName).SequenceEqual(new[] { "方框", "圆框", "箭头", "文字", "马赛克", "撤销", "重做", "取消", "完成" }), "screenshot command row contains exactly nine icons in the requested order");
        check(toolbar.Buttons.All(button => button.Content is FrameworkElement) && toolbar.OptionsBar.Visibility == Visibility.Collapsed, "main commands use vector icons and parameter strip starts collapsed");
        Click(toolbar.Buttons[0]);
        check(selected == ScreenshotTool.Rectangle && toolbar.OptionsBar.Visibility == Visibility.Visible && Options(toolbar).Length == 10, "rectangle expands three sizes, fill and six palette colors");
        Click(Options(toolbar)[3]);
        check(toolbar.Fill, "rectangle fill option selects filled rendering");
        Click(Options(toolbar)[0]);
        check(!toolbar.Fill && toolbar.Thickness == 2, "selecting a line size exits fill mode");
        Click(Options(toolbar)[4]);
        check(toolbar.Color == Color.FromRgb(8, 174, 241), "palette color selection changes the drawing color");
        Click(toolbar.Buttons[1]); Click(Options(toolbar)[3]);
        check(selected == ScreenshotTool.Ellipse && toolbar.Fill && Options(toolbar).Length == 10, "ellipse provides the same three sizes, fill and palette");
        Click(toolbar.Buttons[2]);
        check(!toolbar.Fill && Options(toolbar).Length == 9, "arrow options contain only three stroke sizes and palette");
        Click(toolbar.Buttons[3]); Click(Options(toolbar)[2]);
        check(toolbar.FontSize == 36 && Options(toolbar).Length == 9, "text provides three font-size icons and palette");
        Click(toolbar.Buttons[4]); Click(Options(toolbar)[2]);
        check(toolbar.MosaicSize == 24 && Options(toolbar).Length == 3, "mosaic offers three block sizes without color controls");
        Click(toolbar.Buttons[4]);
        check(selected == ScreenshotTool.Selection && toolbar.OptionsBar.Visibility == Visibility.Collapsed, "clicking the active tool collapses its options and restores selection mode");
        toolbar.UpdateHistory(false, true);
        check(!toolbar.Buttons[5].IsEnabled && toolbar.Buttons[6].IsEnabled, "undo and redo icon states follow document history");

        toolbar.UpdateHistory(true, true);
        toolbar.SetTool(ScreenshotTool.Ellipse);
        var main = Render(toolbar.MainBar);
        var options = Render(toolbar.OptionsBar);
        check(main.PixelWidth == 412 && main.PixelHeight == 48 && options.PixelHeight == 38, "toolbar renders as one compact command row plus a separate compact parameter row");
        var preview = new DrawingVisual();
        using (var dc = preview.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(115, 115, 115)), null, new Rect(0, 0, 450, 116));
            dc.DrawImage(main, new Rect(12, 10, main.PixelWidth, main.PixelHeight));
            dc.DrawImage(options, new Rect(40, 64, options.PixelWidth, options.PixelHeight));
        }
        var bitmap = new RenderTargetBitmap(450, 116, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(preview);
        var output = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "screenshot-toolbar-preview.png");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(output); encoder.Save(file);
    }

    private static BitmapSource Render(Border bar)
    {
        bar.Visibility = Visibility.Visible;
        bar.Measure(new Size(800, 200));
        bar.Arrange(new Rect(new Point(), bar.DesiredSize));
        bar.UpdateLayout();
        var image = new RenderTargetBitmap((int)Math.Ceiling(bar.ActualWidth), (int)Math.Ceiling(bar.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(bar);
        image.Freeze();
        return image;
    }
}
