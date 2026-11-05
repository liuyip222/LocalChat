using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LocalChat;

public sealed class ScreenshotWindow : Window
{
    private readonly CapturedDesktop desktop;
    private readonly Surface surface;
    private readonly Canvas toolsLayer = new();
    private readonly Border toolbar;
    private readonly TextBlock help;
    private Rect selection = Rect.Empty;
    private Rect original;
    private Point dragStart;
    private CaptureEdge edge;
    private bool dragging;
    private bool moving;
    private readonly ScreenshotDocument document = new();
    private readonly ScreenshotToolbar controls;
    private ScreenshotTool tool = ScreenshotTool.Selection;
    private ScreenshotAnnotation? draft;
    private TextBox? textEditor;
    private ScreenshotAnnotation? textDraft;
    private bool drawing;
    private Point strokeStart;
    private Color drawingColor = Colors.Red;
    private double drawingThickness = 3;
    private double drawingFontSize = 24;
    private int drawingMosaicSize = 12;
    private bool drawingFilled;
    private long lastMosaicPreview;
    public BitmapSource? Result { get; private set; }

    public ScreenshotWindow(CapturedDesktop desktop)
    {
        this.desktop = desktop;
        Title = "LocalChat 截图";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Black;
        surface = new Surface(this) { Focusable = true, Cursor = Cursors.Cross };
        var layout = new Grid();
        layout.Children.Add(surface);
        layout.Children.Add(toolsLayer);
        Content = layout;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        controls = new ScreenshotToolbar(Undo, Redo, () => DialogResult = false, Finish);
        toolbar = controls.MainBar;
        controls.ToolChanged += value => { CommitText(); tool = value; surface.Focus(); UpdateTools(); };
        controls.StyleChanged += () => { RefreshTextStyle(); UpdateTools(); };
        UpdateHistoryButtons();
        toolsLayer.Children.Add(toolbar);
        toolsLayer.Children.Add(controls.OptionsBar);
        help = new TextBlock
        {
            Text = "拖动选择范围，再点击图标标注 · 再次点击当前工具可调整选区\nEnter 完成 · Esc / 右键取消 · Ctrl+Z 撤销 · Ctrl+A 全屏",
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(200, 30, 45, 36)),
            Padding = new Thickness(14),
            FontSize = 14,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(help, 24); Canvas.SetTop(help, 24);
        toolsLayer.Children.Add(help);
        Loaded += (_, _) => { ScreenCapture.PositionOverlay(this, desktop); Activate(); surface.Focus(); };
        SizeChanged += (_, _) => { surface.InvalidateVisual(); if (!selection.IsEmpty) UpdateTools(); };
        PreviewKeyDown += OnKeyDown;
        surface.MouseLeftButtonDown += OnDown;
        surface.MouseMove += OnMove;
        surface.MouseLeftButtonUp += OnUp;
        surface.MouseRightButtonUp += (_, _) => { if (textEditor != null) CancelText(); else if (drawing) CancelStroke(); else DialogResult = false; };
        surface.LostMouseCapture += (_, _) => { if (dragging) EndDrag(); if (drawing) CompleteStroke(); };
    }

    private Size Bounds => surface.RenderSize;
    private Int32Rect Pixels => CaptureGeometry.ToPixels(selection, Bounds, desktop.Image.PixelWidth, desktop.Image.PixelHeight);
    private void UpdateHistoryButtons() => controls.UpdateHistory(document.CanUndo || textEditor != null, document.CanRedo);
    private void HideTools()
    {
        toolbar.Visibility = Visibility.Collapsed;
        controls.OptionsBar.Visibility = Visibility.Collapsed;
    }
    private void Undo()
    {
        if (drawing) { CancelStroke(); return; }
        if (textEditor != null) CancelText(); else document.Undo();
        UpdateHistoryButtons(); surface.InvalidateVisual();
    }
    private void Redo()
    {
        if (drawing) CancelStroke();
        CommitText(); document.Redo(); UpdateHistoryButtons(); surface.InvalidateVisual();
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        var point = e.GetPosition(surface);
        CommitText();
        if (tool != ScreenshotTool.Selection && !selection.IsEmpty)
        {
            e.Handled = true;
            if (!selection.Contains(point)) return;
            ReadStyle();
            var pixel = ToSource(point);
            if (tool == ScreenshotTool.Text) { BeginText(pixel); return; }
            strokeStart = pixel;
            drawing = true;
            draft = null;
            lastMosaicPreview = 0;
            HideTools();
            surface.CaptureMouse();
            surface.Focus();
            return;
        }
        if (e.ClickCount == 2 && !selection.IsEmpty && selection.Contains(point)) { Finish(); return; }
        dragStart = point;
        original = selection;
        edge = HitEdge(point);
        moving = edge == CaptureEdge.None && !selection.IsEmpty && selection.Contains(point);
        dragging = true;
        HideTools();
        help.Visibility = Visibility.Collapsed;
        if (!moving && edge == CaptureEdge.None) selection = new Rect(point, point);
        surface.CaptureMouse();
        surface.Focus();
        surface.InvalidateVisual();
        e.Handled = true;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        var point = e.GetPosition(surface);
        if (drawing) { UpdateStroke(point, false); return; }
        if (tool != ScreenshotTool.Selection && !selection.IsEmpty)
        {
            surface.Cursor = tool == ScreenshotTool.Text ? Cursors.IBeam : Cursors.Cross;
            return;
        }
        if (dragging)
        {
            var delta = point - dragStart;
            selection = moving ? CaptureGeometry.Move(original, delta, Bounds)
                : edge != CaptureEdge.None ? CaptureGeometry.Resize(original, delta, edge, Bounds)
                : CaptureGeometry.FromPoints(dragStart, point, Bounds);
            surface.InvalidateVisual();
            return;
        }
        surface.Cursor = HitEdge(point) switch
        {
            CaptureEdge.Left or CaptureEdge.Right => Cursors.SizeWE,
            CaptureEdge.Top or CaptureEdge.Bottom => Cursors.SizeNS,
            CaptureEdge.Left | CaptureEdge.Top or CaptureEdge.Right | CaptureEdge.Bottom => Cursors.SizeNWSE,
            CaptureEdge.Right | CaptureEdge.Top or CaptureEdge.Left | CaptureEdge.Bottom => Cursors.SizeNESW,
            _ => !selection.IsEmpty && selection.Contains(point) ? Cursors.SizeAll : Cursors.Cross
        };
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (drawing)
        {
            UpdateStroke(e.GetPosition(surface), true);
            CompleteStroke();
            surface.ReleaseMouseCapture();
            e.Handled = true;
            return;
        }
        if (!dragging) return;
        EndDrag();
        surface.ReleaseMouseCapture();
        e.Handled = true;
    }
    private void EndDrag()
    {
        dragging = false;
        if (selection.IsEmpty || selection.Width < 1 || selection.Height < 1) { Reset(); return; }
        UpdateTools();
    }

    private Point ToSource(Point p) => new(p.X * desktop.Image.PixelWidth / Bounds.Width, p.Y * desktop.Image.PixelHeight / Bounds.Height);
    private Point ToView(Point p) => new(p.X * Bounds.Width / desktop.Image.PixelWidth, p.Y * Bounds.Height / desktop.Image.PixelHeight);
    private Rect SourceSelection => new(ToSource(selection.TopLeft), ToSource(selection.BottomRight));
    private void ReadStyle()
    {
        drawingColor = controls.Color;
        drawingThickness = controls.Thickness;
        drawingFontSize = controls.FontSize;
        drawingMosaicSize = controls.MosaicSize;
        drawingFilled = controls.Fill;
    }

    private void UpdateStroke(Point point, bool force)
    {
        var bounds = SourceSelection;
        var end = ToSource(point);
        end = new Point(Math.Clamp(end.X, bounds.Left, bounds.Right), Math.Clamp(end.Y, bounds.Top, bounds.Bottom));
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && tool is ScreenshotTool.Rectangle or ScreenshotTool.Ellipse)
        {
            var side = Math.Min(Math.Abs(end.X - strokeStart.X), Math.Abs(end.Y - strokeStart.Y));
            end = new Point(strokeStart.X + Math.Sign(end.X - strokeStart.X) * side, strokeStart.Y + Math.Sign(end.Y - strokeStart.Y) * side);
        }
        if ((end - strokeStart).Length < 1) { draft = null; surface.InvalidateVisual(); return; }
        if (tool == ScreenshotTool.Mosaic)
        {
            if (Math.Abs(end.X - strokeStart.X) < 1 || Math.Abs(end.Y - strokeStart.Y) < 1) { draft = null; surface.InvalidateVisual(); return; }
            if (!force && Environment.TickCount64 - lastMosaicPreview < 90) return;
            lastMosaicPreview = Environment.TickCount64;
            var area = CaptureGeometry.ToPixels(new Rect(strokeStart, end), new Size(desktop.Image.PixelWidth, desktop.Image.PixelHeight), desktop.Image.PixelWidth, desktop.Image.PixelHeight);
            var patch = ScreenshotAnnotationRenderer.CreateMosaicPatch(desktop.Image, document.Items, area, drawingMosaicSize);
            draft = new(tool, new Point(area.X, area.Y), new Point(area.X + area.Width, area.Y + area.Height), drawingColor, MosaicPatch: patch);
        }
        else draft = new(tool, strokeStart, end, drawingColor, drawingThickness, Fill: drawingFilled);
        surface.InvalidateVisual();
    }
    private void CompleteStroke()
    {
        drawing = false;
        if (draft != null) document.Add(draft);
        draft = null;
        UpdateHistoryButtons();
        UpdateTools();
    }
    private void CancelStroke()
    {
        drawing = false;
        draft = null;
        surface.ReleaseMouseCapture();
        UpdateTools();
    }
    private void BeginText(Point point)
    {
        var bounds = SourceSelection;
        textDraft = new(ScreenshotTool.Text, point, bounds.BottomRight, drawingColor, drawingThickness, drawingFontSize);
        var view = ToView(point);
        var availableWidth = Math.Max(1, selection.Right - view.X);
        textEditor = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = drawingFontSize * Bounds.Height / desktop.Image.PixelHeight,
            Foreground = new SolidColorBrush(drawingColor),
            Background = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)),
            BorderBrush = Brushes.SeaGreen,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
            MinWidth = Math.Min(120, availableWidth),
            MaxWidth = availableWidth,
            MaxHeight = Math.Max(1, selection.Bottom - view.Y),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            ToolTip = "Enter 换行；Ctrl+Enter 提交；Esc 取消本次文字；完成截图时自动提交"
        };
        System.Windows.Automation.AutomationProperties.SetName(textEditor, "截图标注文字");
        Canvas.SetLeft(textEditor, view.X); Canvas.SetTop(textEditor, view.Y);
        toolsLayer.Children.Add(textEditor);
        UpdateHistoryButtons();
        textEditor.Focus();
    }
    private void CommitText()
    {
        if (textEditor == null) return;
        RefreshTextStyle();
        if (!string.IsNullOrWhiteSpace(textEditor.Text) && textDraft != null)
            document.Add(textDraft with { Text = textEditor.Text });
        CancelText();
    }
    private void RefreshTextStyle()
    {
        if (textEditor == null || textDraft == null) return;
        ReadStyle();
        textDraft = textDraft with { Color = drawingColor, FontSize = drawingFontSize };
        textEditor.Foreground = new SolidColorBrush(drawingColor);
        textEditor.FontSize = drawingFontSize * Bounds.Height / desktop.Image.PixelHeight;
    }
    private void CancelText()
    {
        if (textEditor == null) return;
        toolsLayer.Children.Remove(textEditor);
        textEditor = null;
        textDraft = null;
        UpdateHistoryButtons();
        surface.Focus();
        surface.InvalidateVisual();
    }

    private CaptureEdge HitEdge(Point p)
    {
        if (selection.IsEmpty || selection.Width <= 0 || selection.Height <= 0) return CaptureEdge.None;
        var expanded = selection;
        expanded.Inflate(7, 7);
        if (!expanded.Contains(p)) return CaptureEdge.None;
        var result = CaptureEdge.None;
        if (Math.Abs(p.X - selection.Left) < 7) result |= CaptureEdge.Left;
        else if (Math.Abs(p.X - selection.Right) < 7) result |= CaptureEdge.Right;
        if (Math.Abs(p.Y - selection.Top) < 7) result |= CaptureEdge.Top;
        else if (Math.Abs(p.Y - selection.Bottom) < 7) result |= CaptureEdge.Bottom;
        return result;
    }

    private void UpdateTools()
    {
        if (selection.IsEmpty || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var options = controls.OptionsBar;
        toolbar.MaxWidth = options.MaxWidth = Math.Max(1, Bounds.Width - 24);
        toolbar.Visibility = Visibility.Visible;
        options.Visibility = tool == ScreenshotTool.Selection ? Visibility.Collapsed : Visibility.Visible;
        toolbar.Measure(new Size(toolbar.MaxWidth, double.PositiveInfinity));
        if (options.Visibility == Visibility.Visible) options.Measure(new Size(options.MaxWidth, double.PositiveInfinity));
        var optionsHeight = options.Visibility == Visibility.Visible ? options.DesiredSize.Height + 6 : 0;
        var totalHeight = toolbar.DesiredSize.Height + optionsHeight;
        var left = Math.Clamp(selection.Right - toolbar.DesiredSize.Width, 12, Math.Max(12, Bounds.Width - toolbar.DesiredSize.Width - 12));
        var top = selection.Bottom + 10;
        if (top + totalHeight > Bounds.Height - 12) top = selection.Top - totalHeight - 10;
        top = Math.Clamp(top, 12, Math.Max(12, Bounds.Height - totalHeight - 12));
        Canvas.SetLeft(toolbar, left); Canvas.SetTop(toolbar, top);
        if (options.Visibility == Visibility.Visible)
        {
            var scale = Math.Min(1, toolbar.DesiredSize.Width / 412);
            var optionLeft = left + 8 + Math.Max(0, (int)tool - 1) * 44 * scale;
            Canvas.SetLeft(options, Math.Clamp(optionLeft, 12, Math.Max(12, Bounds.Width - options.DesiredSize.Width - 12)));
            Canvas.SetTop(options, top + toolbar.DesiredSize.Height + 6);
        }
        surface.InvalidateVisual();
    }

    private void Reset()
    {
        CancelText();
        document.Clear();
        draft = null;
        tool = ScreenshotTool.Selection;
        controls.SetTool(ScreenshotTool.Selection);
        UpdateHistoryButtons();
        selection = Rect.Empty;
        HideTools();
        help.Visibility = Visibility.Visible;
        surface.Focus();
        surface.InvalidateVisual();
    }
    private void Finish()
    {
        if (selection.IsEmpty || selection.Width <= 0 || selection.Height <= 0) return;
        CommitText();
        Result = ScreenshotAnnotationRenderer.Render(desktop.Image, Pixels, document.Items);
        DialogResult = true;
    }
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (textEditor != null) CancelText();
            else if (drawing) CancelStroke();
            else DialogResult = false;
            return;
        }
        if (textEditor != null && textEditor.IsKeyboardFocusWithin)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { e.Handled = true; CommitText(); }
            return;
        }
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key is Key.Z or Key.Y)
        {
            e.Handled = true;
            if (e.Key == Key.Y || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) Redo(); else Undo();
            return;
        }
        if (e.Key == Key.Enter) { e.Handled = true; Finish(); }
        else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            selection = new Rect(new Point(), Bounds);
            help.Visibility = Visibility.Collapsed;
            UpdateTools();
        }
        else if (!selection.IsEmpty && e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
            var dx = (e.Key == Key.Right ? step : e.Key == Key.Left ? -step : 0) * Bounds.Width / desktop.Image.PixelWidth;
            var dy = (e.Key == Key.Down ? step : e.Key == Key.Up ? -step : 0) * Bounds.Height / desktop.Image.PixelHeight;
            selection = CaptureGeometry.Move(selection, new Vector(dx, dy), Bounds);
            e.Handled = true;
            UpdateTools();
        }
    }

    private sealed class Surface(ScreenshotWindow owner) : FrameworkElement
    {
        private static readonly Brush Shade = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0));
        private static readonly Pen Outline = new(new SolidColorBrush(Color.FromRgb(45, 166, 105)), 1.5);
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            var bounds = new Rect(new Point(), RenderSize);
            dc.DrawImage(owner.desktop.Image, bounds);
            var r = owner.selection;
            if (r.IsEmpty) { dc.DrawRectangle(Shade, null, bounds); return; }
            // Same source-pixel renderer is used for preview and final output.
            dc.PushClip(new RectangleGeometry(r));
            dc.PushTransform(new ScaleTransform(RenderSize.Width / owner.desktop.Image.PixelWidth, RenderSize.Height / owner.desktop.Image.PixelHeight));
            foreach (var item in owner.document.Items) ScreenshotAnnotationRenderer.Draw(dc, item);
            if (owner.draft != null) ScreenshotAnnotationRenderer.Draw(dc, owner.draft);
            dc.Pop(); dc.Pop();
            var mask = new GeometryGroup { FillRule = FillRule.EvenOdd };
            mask.Children.Add(new RectangleGeometry(bounds));
            mask.Children.Add(new RectangleGeometry(r));
            dc.DrawGeometry(Shade, null, mask);
            dc.DrawRectangle(null, Outline, r);
            if (owner.tool != ScreenshotTool.Selection) return;
            foreach (var x in new[] { r.Left, r.Left + r.Width / 2, r.Right })
                foreach (var y in new[] { r.Top, r.Top + r.Height / 2, r.Bottom })
                {
                    if (x == r.Left + r.Width / 2 && y == r.Top + r.Height / 2) continue;
                    dc.DrawRectangle(Brushes.White, Outline, new Rect(x - 3, y - 3, 6, 6));
                }
        }
    }
}
