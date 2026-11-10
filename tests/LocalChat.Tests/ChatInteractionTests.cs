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
            var pixels = new byte[640 * 4];
            pixels[2] = 255;
            pixels[3] = 128;
            var original = BitmapSource.Create(640, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 640 * 4);
            var originalEncoder = new PngBitmapEncoder();
            originalEncoder.Frames.Add(BitmapFrame.Create(original));
            var originalPath = Path.Combine(root, "original.png");
            using (var file = File.Create(originalPath)) originalEncoder.Save(file);
            var copyData = ClipboardImages.CreateData(originalPath);
            File.Delete(originalPath);
            var copied = ClipboardImages.Read(copyData)!;
            check(copied.PixelWidth == 640 && copied.PixelHeight == 1 && copied.IsFrozen,
                "image copy loads full-resolution original and releases the source file");
            var copiedPixels = new byte[640 * 4];
            copied.CopyPixels(copiedPixels, 640 * 4, 0);
            check(copiedPixels.SequenceEqual(pixels), "PNG image copy preserves original pixels and transparency");
            check(copyData.GetDataPresent("PNG", false) && copyData.GetDataPresent("image/png", false)
                && copyData.GetData(DataFormats.Bitmap, false) is BitmapSource { PixelWidth: 640 },
                "image copy publishes PNG formats and a full-resolution Windows bitmap fallback");
            var mimeData = new DataObject();
            mimeData.SetData("image/png", copyData.GetData("image/png", false), false);
            check(ClipboardImages.Read(mimeData)?.PixelWidth == 640, "image/png copy format can be decoded independently");
            var missingRejected = false;
            try { ClipboardImages.CreateData(originalPath); }
            catch (FileNotFoundException) { missingRejected = true; }
            check(missingRejected, "missing image original is rejected before accessing the clipboard");

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
            var attachments = ((StackPanel)bubble.Child).Children.OfType<ItemsControl>().Single();
            var attachmentContent = (Border)attachments.ItemTemplate.LoadContent();
            var attachmentPanel = (StackPanel)attachmentContent.Child;
            var actions = attachmentPanel.Children.OfType<WrapPanel>().Single();
            var copyButton = actions.Children.OfType<Button>().Single(b => Equals(b.Content, "复制图片"));
            var attachment = new Attachment { Kind = "image", Thumbnail = image };
            attachmentContent.DataContext = attachment;
            attachmentContent.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            copyButton.GetBindingExpression(UIElement.VisibilityProperty)!.UpdateTarget();
            check(copyButton.Visibility == Visibility.Visible && attachmentPanel.Children.IndexOf(actions)
                > attachmentPanel.Children.IndexOf(attachmentPanel.Children.OfType<Image>().Single()),
                "image attachment exposes Copy Image in the action row below its preview");
            attachmentContent.DataContext = new Attachment { Kind = "image" };
            attachmentContent.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            copyButton.GetBindingExpression(UIElement.VisibilityProperty)!.UpdateTarget();
            check(copyButton.Visibility == Visibility.Visible, "image copy remains available without a cached thumbnail");
            foreach (var kind in new[] { "file", "video" })
            {
                attachmentContent.DataContext = new Attachment { Kind = kind };
                attachmentContent.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                copyButton.GetBindingExpression(UIElement.VisibilityProperty)!.UpdateTarget();
                check(copyButton.Visibility == Visibility.Collapsed, $"{kind} attachment does not expose Copy Image");
            }
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
