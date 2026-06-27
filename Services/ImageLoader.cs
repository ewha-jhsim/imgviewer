using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using DBitmap = System.Drawing.Bitmap;
using DImage = System.Drawing.Image;
using ISImage = SixLabors.ImageSharp.Image;

namespace ImgViewer.Services;

/// <summary>
/// Loads images into GDI+ objects. GDI+ natively handles png/jpg/gif/bmp/tiff/ico;
/// WebP is decoded with the pure-managed ImageSharp library and converted to a Bitmap.
/// </summary>
public static class ImageLoader
{
    // Extensions we expose to the OS and accept on the command line.
    public static readonly string[] SupportedExtensions =
    {
        ".png", ".jpg", ".jpeg", ".jpe", ".jfif",
        ".gif", ".bmp", ".dib", ".webp",
        ".tif", ".tiff", ".ico",
    };

    public static bool IsSupported(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return Array.IndexOf(SupportedExtensions, ext) >= 0;
    }

    /// <summary>
    /// Loads a file into a GDI+ <see cref="DImage"/>. The returned image owns its pixel
    /// data in memory and does not keep the source file locked, so the file can be
    /// renamed, deleted, or overwritten while the image is displayed. Animated GIFs keep
    /// all frames so <see cref="System.Drawing.ImageAnimator"/> can play them.
    /// </summary>
    public static DImage Load(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();

        if (ext == ".webp")
            return LoadWithImageSharp(path);

        try
        {
            // Read into memory first so GDI+ never holds a lock on the file.
            byte[] bytes = File.ReadAllBytes(path);
            // The stream must stay alive for the image's lifetime; the Image keeps a
            // reference to it, so it is collected together with the image.
            var ms = new MemoryStream(bytes, writable: false);
            return DImage.FromStream(ms, useEmbeddedColorManagement: true, validateImageData: true);
        }
        catch (Exception)
        {
            // Some files carry a misleading extension; fall back to ImageSharp's
            // content-based detection before giving up.
            return LoadWithImageSharp(path);
        }
    }

    private static DBitmap LoadWithImageSharp(string path)
    {
        using SixLabors.ImageSharp.Image<Rgba32> img = ISImage.Load<Rgba32>(path);
        return ToBitmap(img);
    }

    /// <summary>Converts an ImageSharp RGBA image into a 32bpp ARGB GDI+ bitmap.</summary>
    private static unsafe DBitmap ToBitmap(SixLabors.ImageSharp.Image<Rgba32> img)
    {
        var bmp = new DBitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
        var rect = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
        BitmapData data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            img.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    Span<Rgba32> src = accessor.GetRowSpan(y);
                    byte* dst = (byte*)data.Scan0 + (long)y * data.Stride;
                    for (int x = 0; x < src.Length; x++)
                    {
                        Rgba32 px = src[x];
                        int o = x * 4;
                        // GDI+ Format32bppArgb is stored as B, G, R, A in memory.
                        dst[o + 0] = px.B;
                        dst[o + 1] = px.G;
                        dst[o + 2] = px.R;
                        dst[o + 3] = px.A;
                    }
                }
            });
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        return bmp;
    }
}
