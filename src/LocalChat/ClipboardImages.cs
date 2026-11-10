using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace LocalChat;

public static class ClipboardImages
{
    // Read the original at full resolution and close the file before publishing.
    // PNG preserves transparency; Bitmap is a fallback for traditional Windows apps.
    public static DataObject CreateData(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("附件原件不存在，可能已被外部移动或删除。", path);
        BitmapSource image;
        using (var source = File.OpenRead(path)) image = Decode(source);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var png = new MemoryStream();
        encoder.Save(png);
        var bytes = png.ToArray();
        var data = new DataObject();
        data.SetData("PNG", new MemoryStream(bytes, writable: false), false);
        data.SetData("image/png", new MemoryStream(bytes, writable: false), false);
        data.SetData(DataFormats.Bitmap, image, true);
        return data;
    }

    public static async Task CopyAsync(string path)
    {
        var data = CreateData(path);
        for (var attempt = 0; ; attempt++)
        {
            try { Clipboard.SetDataObject(data, true); return; }
            catch (ExternalException) when (attempt < 4) { await Task.Delay(100); }
        }
    }

    // Windows Snipping Tool can publish PNG without a text format. Decode it
    // independently of TextBox's built-in (text-only) Paste command.
    public static BitmapSource? Read(IDataObject data)
    {
        foreach (var format in new[] { "PNG", "image/png" })
        {
            if (!data.GetDataPresent(format, false)) continue;
            var value = data.GetData(format, false);
            if (value is byte[] bytes)
            {
                using var stream = new MemoryStream(bytes);
                return Decode(stream);
            }
            if (value is Stream source)
            {
                var position = source.CanSeek ? source.Position : 0;
                try
                {
                    if (source.CanSeek) source.Position = 0;
                    return Decode(source);
                }
                finally { if (source.CanSeek) source.Position = position; }
            }
        }
        return data.GetDataPresent(DataFormats.Bitmap, true)
            ? data.GetData(DataFormats.Bitmap, true) as BitmapSource : null;
    }

    private static BitmapSource Decode(Stream stream)
    {
        var image = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad).Frames[0];
        image.Freeze();
        return image;
    }
}
