using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalChat;

internal static class ChatInteractionTests
{
    // Called on the screenshot tests' STA thread. Uses synthetic clipboard data;
    // never reads or overwrites the user's actual clipboard.
    internal static void Run(Action<bool, string> check)
    {
        var image = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { 0, 0, 255, 255, 0, 255, 0, 255 }, 8);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var png = new MemoryStream();
        encoder.Save(png);
        var data = new DataObject();
        data.SetData("PNG", png, false);
        var decoded = ClipboardImages.Read(data);
        check(decoded is { PixelWidth: 2, PixelHeight: 1, IsFrozen: true }, "image-only Snipping Tool PNG stream decodes without a text format");
        check(png.Position == png.Length && png.CanRead, "PNG decoding restores stream position and leaves clipboard stream open");
        var bytes = new DataObject();
        bytes.SetData("image/png", png.ToArray(), false);
        check(ClipboardImages.Read(bytes)?.PixelWidth == 2, "PNG byte-array clipboard format is supported");
        var bitmap = new DataObject();
        bitmap.SetData(DataFormats.Bitmap, image);
        check(ClipboardImages.Read(bitmap) == image, "standard bitmap clipboard data is supported");
        var text = new DataObject(DataFormats.UnicodeText, "普通文本");
        check(ClipboardImages.Read(text) == null, "plain text is not mistaken for an image");

        var root = Path.Combine(Path.GetTempPath(), "LocalChatUiTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var window = new MainWindow(new Settings(), new ChatStore(root));
            var list = (ListBox)window.FindName("MessagesList");
            var content = (FrameworkElement)list.ItemTemplate.LoadContent();
            content.DataContext = new Message { Text = "可选中文字" };
            content.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            content.Measure(new Size(700, 400));
            content.Arrange(new Rect(content.DesiredSize));
            var bubble = (Border)content.FindName("MessageBubble");
            var messageText = ((StackPanel)bubble.Child).Children.OfType<TextBox>().Single();
            messageText.GetBindingExpression(TextBox.TextProperty)!.UpdateTarget();
            content.Measure(new Size(700, 400));
            content.Arrange(new Rect(content.DesiredSize));
            messageText.Select(1, 2);
            check(messageText.IsReadOnly && messageText.SelectedText == "选中", $"message text is selectable without allowing edits (text='{messageText.Text}', selected='{messageText.SelectedText}')");
            check(messageText.Padding == new Thickness(0) && bubble.Padding == new Thickness(9, 7, 9, 7), "bubble uses compact symmetric padding without nested text padding");
            check(bubble.ActualWidth < 180, "short message bubble sizes to its contents");
            check(((MenuItem)bubble.ContextMenu.Items[0]).Header.Equals("复制"), "message context menu uses the shorter Copy label");
            check(ReferenceEquals(messageText.ContextMenu, bubble.ContextMenu), "selectable message text retains the bubble Copy/Edit/Delete menu");
            var composer = (TextBox)window.FindName("Composer");
            check(composer.CommandBindings.Cast<CommandBinding>().Any(b => b.Command == ApplicationCommands.Paste), "composer overrides text-only Paste command so image-only clipboard can execute");
            window.Close();
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }
}
