using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalChat;

internal static class ScreenshotAnnotationTests
{
    internal static void Run(Action<bool, string> check)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { Test(check); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) throw new InvalidOperationException("Screenshot annotation tests failed.", error);
    }

    private static Color Pixel(BitmapSource image, int x, int y)
    {
        byte[] pixel = new byte[4];
        image.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }
    private static bool Red(Color c) => c.R > 200 && c.G < 80 && c.B < 80;
    private static void Test(Action<bool, string> check)
    {
        const int size = 64;
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var offset = (y * size + x) * 4;
                pixels[offset] = 80;
                pixels[offset + 1] = (byte)(y * 3);
                pixels[offset + 2] = (byte)(x * 3);
                pixels[offset + 3] = 255;
            }
        var source = BitmapSource.Create(size, size, 96, 96, PixelFormats.Pbgra32, null, pixels, size * 4);
        source.Freeze();
        var area = new Int32Rect(0, 0, size, size);
        var rectangle = new ScreenshotAnnotation(ScreenshotTool.Rectangle, new Point(8, 8), new Point(56, 56), Colors.Red, 4);
        var rendered = ScreenshotAnnotationRenderer.Render(source, area, [rectangle]);
        check(Red(Pixel(rendered, 8, 30)) && Pixel(rendered, 30, 30) == Pixel(source, 30, 30), "rectangle draws colored border without filling the interior");
        var ellipse = ScreenshotAnnotationRenderer.Render(source, area, [rectangle with { Tool = ScreenshotTool.Ellipse }]);
        check(Red(Pixel(ellipse, 32, 8)) && !Red(Pixel(ellipse, 8, 8)), "ellipse draws an elliptical outline rather than a rectangle");
        var arrow = new ScreenshotAnnotation(ScreenshotTool.Arrow, new Point(5, 32), new Point(58, 32), Colors.Red, 3);
        var arrowImage = ScreenshotAnnotationRenderer.Render(source, area, [arrow]);
        check(Red(Pixel(arrowImage, 30, 32)) && Red(Pixel(arrowImage, 52, 33)), "arrow renders both shaft and filled arrowhead");
        var text = new ScreenshotAnnotation(ScreenshotTool.Text, new Point(2, 2), new Point(62, 62), Colors.Red, FontSize: 18, Text: "测试A", Bold: true);
        var textImage = ScreenshotAnnotationRenderer.Render(source, area, [text]);
        var textPixels = new byte[pixels.Length];
        textImage.CopyPixels(textPixels, size * 4, 0);
        check(Enumerable.Range(0, size * size).Any(i => textPixels[i * 4 + 2] > 200 && textPixels[i * 4 + 1] < 80), "Chinese and Latin text render with the selected color and font style");
        var block = new Int32Rect(0, 0, 24, 24);
        var patch = ScreenshotAnnotationRenderer.CreateMosaicPatch(source, [], block, 8);
        check(Pixel(patch, 0, 0) == Pixel(patch, 7, 7) && Pixel(patch, 0, 0) != Pixel(patch, 8, 8), "mosaic produces uniform averaged blocks with configurable size");
        var patchOverMark = ScreenshotAnnotationRenderer.CreateMosaicPatch(source, [rectangle], block, 8);
        check(Pixel(patchOverMark, 8, 8) != Pixel(patch, 8, 8), "mosaic includes existing annotations in the obscured pixels");
        var mosaic = new ScreenshotAnnotation(ScreenshotTool.Mosaic, new Point(0, 0), new Point(24, 24), Colors.Red, MosaicPatch: patch);
        var combined = ScreenshotAnnotationRenderer.Render(source, area, [mosaic, rectangle]);
        check(Red(Pixel(combined, 8, 16)), "later annotations draw above earlier mosaic patches");
        var cropped = ScreenshotAnnotationRenderer.Render(source, new Int32Rect(8, 8, 24, 16), [rectangle]);
        check(cropped.PixelWidth == 24 && cropped.PixelHeight == 16 && Red(Pixel(cropped, 0, 8)), "annotated output uses exact crop dimensions and source-pixel offsets");
        check(Pixel(source, 8, 8) == Color.FromRgb(24, 24, 80), "annotation and mosaic do not modify the source image");
        var thick = ScreenshotAnnotationRenderer.Render(source, area, [rectangle with { Color = Colors.Blue, Thickness = 8 }]);
        check(Pixel(thick, 10, 30).B > 220 && Pixel(thick, 10, 30).R < 60, "line color and thickness affect the final pixels");

        var document = new ScreenshotDocument();
        document.Add(rectangle); document.Add(arrow); document.Undo();
        check(document.Items.Count == 1 && document.CanRedo, "undo removes the last annotation and enables redo");
        document.Redo();
        check(document.Items.Count == 2 && document.Items[1] == arrow, "redo restores the same annotation and order");
        document.Undo(); document.Add(text);
        check(!document.CanRedo && document.Items[1] == text, "a new annotation after undo discards the redo branch");
        document.Clear();
        check(!document.CanUndo && !document.CanRedo && document.Items.Count == 0, "reselect clears both annotation history stacks");
        var filled = ScreenshotAnnotationRenderer.Render(source, area, [rectangle with { Fill = true }]);
        check(Red(Pixel(filled, 30, 30)), "filled rectangle paints the interior with selected color");
        var filledCircle = ScreenshotAnnotationRenderer.Render(source, area, [rectangle with { Tool = ScreenshotTool.Ellipse, Fill = true }]);
        check(Red(Pixel(filledCircle, 30, 30)) && !Red(Pixel(filledCircle, 8, 8)), "filled ellipse paints only its elliptical interior");
        ScreenshotToolbarTests.Run(check);
        ChatInteractionTests.Run(check);
    }
}
