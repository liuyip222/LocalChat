using System.Windows;

namespace LocalChat;

[Flags]
public enum CaptureEdge { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8 }

public static class CaptureGeometry
{
    public static Rect FromPoints(Point a, Point b, Size bounds)
    {
        a = Clamp(a, bounds);
        b = Clamp(b, bounds);
        return new Rect(a, b);
    }

    public static Rect Move(Rect rectangle, Vector delta, Size bounds) => new(
        Math.Clamp(rectangle.X + delta.X, 0, Math.Max(0, bounds.Width - rectangle.Width)),
        Math.Clamp(rectangle.Y + delta.Y, 0, Math.Max(0, bounds.Height - rectangle.Height)),
        rectangle.Width, rectangle.Height);

    public static Rect Resize(Rect rectangle, Vector delta, CaptureEdge edge, Size bounds)
    {
        var left = rectangle.Left;
        var top = rectangle.Top;
        var right = rectangle.Right;
        var bottom = rectangle.Bottom;
        if (edge.HasFlag(CaptureEdge.Left)) left = Math.Clamp(left + delta.X, 0, Math.Max(0, right - 1));
        if (edge.HasFlag(CaptureEdge.Top)) top = Math.Clamp(top + delta.Y, 0, Math.Max(0, bottom - 1));
        if (edge.HasFlag(CaptureEdge.Right)) right = Math.Clamp(right + delta.X, Math.Min(left + 1, bounds.Width), bounds.Width);
        if (edge.HasFlag(CaptureEdge.Bottom)) bottom = Math.Clamp(bottom + delta.Y, Math.Min(top + 1, bounds.Height), bounds.Height);
        return new Rect(new Point(left, top), new Point(right, bottom));
    }

    public static Int32Rect ToPixels(Rect rectangle, Size displayedSize, int pixelWidth, int pixelHeight)
    {
        if (rectangle.IsEmpty || rectangle.Width <= 0 || rectangle.Height <= 0 || displayedSize.Width <= 0 || displayedSize.Height <= 0)
            throw new ArgumentException("请先选择截图区域。");
        const double roundingTolerance = 1e-7;
        var left = Math.Clamp((int)Math.Floor(rectangle.Left * pixelWidth / displayedSize.Width + roundingTolerance), 0, pixelWidth - 1);
        var top = Math.Clamp((int)Math.Floor(rectangle.Top * pixelHeight / displayedSize.Height + roundingTolerance), 0, pixelHeight - 1);
        var right = Math.Clamp((int)Math.Ceiling(rectangle.Right * pixelWidth / displayedSize.Width - roundingTolerance), left + 1, pixelWidth);
        var bottom = Math.Clamp((int)Math.Ceiling(rectangle.Bottom * pixelHeight / displayedSize.Height - roundingTolerance), top + 1, pixelHeight);
        return new Int32Rect(left, top, right - left, bottom - top);
    }

    private static Point Clamp(Point p, Size bounds) => new(Math.Clamp(p.X, 0, bounds.Width), Math.Clamp(p.Y, 0, bounds.Height));
}
