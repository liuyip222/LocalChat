using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalChat;

// Explicit, opt-in smoke-test window. Uses a generated image, never reads the desktop or clipboard.
internal static class ScreenshotUiPreview
{
    internal static void Run()
    {
        var thread = new Thread(() =>
        {
            Console.WriteLine("SCREENSHOT_UI: creating synthetic bitmap");
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.LightBlue, null, new Rect(0, 0, 1280, 800));
                dc.DrawRectangle(Brushes.SeaGreen, null, new Rect(100, 100, 400, 300));
                dc.DrawRectangle(Brushes.Goldenrod, null, new Rect(700, 450, 400, 250));
            }
            var source = new RenderTargetBitmap(1280, 800, 96, 96, PixelFormats.Pbgra32);
            source.Render(visual);
            source.Freeze();
            Console.WriteLine("SCREENSHOT_UI: creating selection window");
            var window = new ScreenshotWindow(new CapturedDesktop(source, 0, 0)) { ShowInTaskbar = true };
            Console.WriteLine("SCREENSHOT_UI: opening selection window");
            if (window.ShowDialog() == true && window.Result is { } result)
            {
                var output = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "screenshot-ui-result.png");
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(result));
                using var file = File.Create(output);
                encoder.Save(file);
                Console.WriteLine($"SCREENSHOT_UI: {result.PixelWidth}x{result.PixelHeight}; {output}");
            }
            else Console.WriteLine("SCREENSHOT_UI: canceled; clipboard and user files unchanged.");
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }
}
