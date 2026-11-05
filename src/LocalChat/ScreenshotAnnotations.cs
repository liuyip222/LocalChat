using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LocalChat;

public enum ScreenshotTool { Selection, Rectangle, Ellipse, Arrow, Text, Mosaic }

// Coordinates, font sizes, stroke widths and mosaic blocks are all in source-image pixels.
public sealed record ScreenshotAnnotation(
    ScreenshotTool Tool, Point Start, Point End, Color Color, double Thickness = 3,
    double FontSize = 24, string Text = "", bool Bold = false, BitmapSource? MosaicPatch = null, bool Fill = false);

public sealed class ScreenshotDocument
{
    private readonly List<ScreenshotAnnotation> items = [];
    private readonly Stack<ScreenshotAnnotation> redo = [];
    public ReadOnlyCollection<ScreenshotAnnotation> Items { get; }
    public bool CanUndo => items.Count > 0;
    public bool CanRedo => redo.Count > 0;

    public ScreenshotDocument() => Items = items.AsReadOnly();
    public void Add(ScreenshotAnnotation annotation) { items.Add(annotation); redo.Clear(); }
    public void Undo()
    {
        if (!CanUndo) return;
        redo.Push(items[^1]);
        items.RemoveAt(items.Count - 1);
    }
    public void Redo() { if (redo.TryPop(out var item)) items.Add(item); }
    public void Clear() { items.Clear(); redo.Clear(); }
}

public static class ScreenshotAnnotationRenderer
{
    public static BitmapSource Render(BitmapSource source, Int32Rect crop, IReadOnlyList<ScreenshotAnnotation> annotations)
    {
        if (crop.X < 0 || crop.Y < 0 || crop.Width < 1 || crop.Height < 1
            || (long)crop.X + crop.Width > source.PixelWidth || (long)crop.Y + crop.Height > source.PixelHeight)
            throw new ArgumentException("截图区域必须位于图像内。");
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, crop.Width, crop.Height)));
            dc.PushTransform(new TranslateTransform(-crop.X, -crop.Y));
            dc.DrawImage(source, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
            foreach (var annotation in annotations) Draw(dc, annotation);
            dc.Pop(); dc.Pop();
        }
        var output = new RenderTargetBitmap(crop.Width, crop.Height, 96, 96, PixelFormats.Pbgra32);
        output.Render(visual);
        output.Freeze();
        return output;
    }

    public static void Draw(DrawingContext dc, ScreenshotAnnotation item)
    {
        var brush = new SolidColorBrush(item.Color);
        var pen = new Pen(brush, item.Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        var rectangle = new Rect(item.Start, item.End);
        switch (item.Tool)
        {
            case ScreenshotTool.Rectangle:
                dc.DrawRectangle(item.Fill ? brush : null, item.Fill ? null : pen, rectangle);
                break;
            case ScreenshotTool.Ellipse:
                dc.DrawEllipse(item.Fill ? brush : null, item.Fill ? null : pen, new Point(rectangle.Left + rectangle.Width / 2, rectangle.Top + rectangle.Height / 2), rectangle.Width / 2, rectangle.Height / 2);
                break;
            case ScreenshotTool.Arrow:
                var vector = item.End - item.Start;
                if (vector.Length < 0.5) return;
                var length = vector.Length;
                vector.Normalize();
                var normal = new Vector(-vector.Y, vector.X);
                var head = Math.Min(length * 0.65, Math.Max(10, item.Thickness * 4));
                dc.DrawLine(pen, item.Start, item.End);
                var geometry = new StreamGeometry();
                using (var path = geometry.Open())
                {
                    path.BeginFigure(item.End, true, true);
                    path.LineTo(item.End - vector * head + normal * head * 0.45, true, false);
                    path.LineTo(item.End - vector * head - normal * head * 0.45, true, false);
                }
                dc.DrawGeometry(brush, null, geometry);
                break;
            case ScreenshotTool.Text:
                if (string.IsNullOrEmpty(item.Text)) return;
                var text = new FormattedText(item.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, item.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal), item.FontSize, brush, 1)
                { MaxTextWidth = Math.Max(1, rectangle.Width) };
                dc.PushClip(new RectangleGeometry(rectangle));
                dc.DrawText(text, item.Start);
                dc.Pop();
                break;
            case ScreenshotTool.Mosaic:
                if (item.MosaicPatch != null) dc.DrawImage(item.MosaicPatch, rectangle);
                break;
        }
    }

    public static BitmapSource CreateMosaicPatch(BitmapSource source, IReadOnlyList<ScreenshotAnnotation> existing, Int32Rect area, int blockSize)
    {
        if (blockSize is < 2 or > 128) throw new ArgumentOutOfRangeException(nameof(blockSize));
        // Mosaic the current composition, including previous marks/text, not just the original desktop.
        var composed = Render(source, area, existing);
        var stride = checked(area.Width * 4);
        var pixels = new byte[checked(stride * area.Height)];
        composed.CopyPixels(pixels, stride, 0);
        for (var y = 0; y < area.Height; y += blockSize)
            for (var x = 0; x < area.Width; x += blockSize)
            {
                var right = Math.Min(area.Width, x + blockSize);
                var bottom = Math.Min(area.Height, y + blockSize);
                long b = 0, g = 0, r = 0, a = 0;
                for (var yy = y; yy < bottom; yy++)
                    for (var xx = x; xx < right; xx++)
                    {
                        var index = yy * stride + xx * 4;
                        b += pixels[index]; g += pixels[index + 1]; r += pixels[index + 2]; a += pixels[index + 3];
                    }
                var count = (right - x) * (bottom - y);
                for (var yy = y; yy < bottom; yy++)
                    for (var xx = x; xx < right; xx++)
                    {
                        var index = yy * stride + xx * 4;
                        pixels[index] = (byte)(b / count); pixels[index + 1] = (byte)(g / count);
                        pixels[index + 2] = (byte)(r / count); pixels[index + 3] = (byte)(a / count);
                    }
            }
        var output = BitmapSource.Create(area.Width, area.Height, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        output.Freeze();
        return output;
    }
}
