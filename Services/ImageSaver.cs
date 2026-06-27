using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using SixLabors.ImageSharp; // for the Image.Save(path, encoder) extension
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using DBitmap = System.Drawing.Bitmap;
using DRectangle = System.Drawing.Rectangle;

namespace ImgViewer.Services;

/// <summary>Saves a GDI+ bitmap back to disk, picking the encoder from the extension.</summary>
public static class ImageSaver
{
    /// <summary>Extensions we can write back out.</summary>
    public static readonly string[] WritableExtensions =
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp",
    };

    public static bool CanSave(string path) =>
        WritableExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static void Save(DBitmap bitmap, string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        switch (ext)
        {
            case ".webp":
                SaveWebp(bitmap, path);
                break;
            case ".png":
                bitmap.Save(path, ImageFormat.Png);
                break;
            case ".bmp":
                bitmap.Save(path, ImageFormat.Bmp);
                break;
            case ".gif":
                bitmap.Save(path, ImageFormat.Gif);
                break;
            case ".tif":
            case ".tiff":
                bitmap.Save(path, ImageFormat.Tiff);
                break;
            case ".jpg":
            case ".jpeg":
                SaveJpeg(bitmap, path, quality: 92L);
                break;
            default:
                // Unknown/unsupported target: keep data lossless as PNG.
                bitmap.Save(path, ImageFormat.Png);
                break;
        }
    }

    private static void SaveJpeg(DBitmap bitmap, string path, long quality)
    {
        // JPEG has no alpha channel; composite onto white so transparency doesn't go black.
        using var flat = new DBitmap(bitmap.Width, bitmap.Height, PixelFormat.Format24bppRgb);
        using (Graphics g = Graphics.FromImage(flat))
        {
            g.Clear(System.Drawing.Color.White);
            g.DrawImageUnscaled(bitmap, 0, 0);
        }

        ImageCodecInfo? jpeg = ImageCodecInfo.GetImageEncoders()
            .FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var ep = new EncoderParameters(1);
        ep.Param[0] = new EncoderParameter(Encoder.Quality, quality);
        if (jpeg is not null)
            flat.Save(path, jpeg, ep);
        else
            flat.Save(path, ImageFormat.Jpeg);
    }

    private static unsafe void SaveWebp(DBitmap bitmap, string path)
    {
        using var argb = new DBitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(argb))
            g.DrawImageUnscaled(bitmap, 0, 0);

        var rect = new DRectangle(0, 0, argb.Width, argb.Height);
        BitmapData data = argb.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            using var img = new SixLabors.ImageSharp.Image<Rgba32>(argb.Width, argb.Height);
            img.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    Span<Rgba32> dstRow = accessor.GetRowSpan(y);
                    byte* srcRow = (byte*)data.Scan0 + (long)y * data.Stride;
                    for (int x = 0; x < dstRow.Length; x++)
                    {
                        int o = x * 4; // source is BGRA
                        dstRow[x] = new Rgba32(srcRow[o + 2], srcRow[o + 1], srcRow[o + 0], srcRow[o + 3]);
                    }
                }
            });
            img.Save(path, new WebpEncoder { Quality = 90 });
        }
        finally
        {
            argb.UnlockBits(data);
        }
    }
}
