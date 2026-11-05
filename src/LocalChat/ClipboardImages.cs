using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace LocalChat;

public static class ClipboardImages
{
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
