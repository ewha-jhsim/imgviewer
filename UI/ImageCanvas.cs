using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ImgViewer.UI;

/// <summary>
/// Renders an image with a checkerboard backdrop (so PNG transparency is visible),
/// supports fit/zoom/pan, plays animated GIFs, and hosts an interactive crop selection.
/// </summary>
public sealed class ImageCanvas : Control
{
    private Image? _image;
    private bool _animating;

    private float _zoom = 1f;
    private PointF _pan = PointF.Empty; // extra translation applied on top of centering
    private bool _panning;
    private Point _panStart;
    private PointF _panOrigin;

    private TextureBrush? _checker;

    // --- Crop state ---
    private bool _cropMode;
    private bool _dragging;
    private Point _dragStart;
    private Rectangle _selectionClient; // selection in client coordinates

    public event EventHandler? ViewChanged;

    public ImageCanvas()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(48, 48, 48);
        TabStop = false;
    }

    public bool HasImage => _image is not null;
    public Size ImageSize => _image?.Size ?? Size.Empty;
    public Image? CurrentImage => _image;
    public float Zoom => _zoom;
    public bool CropMode => _cropMode;

    /// <summary>Sets the displayed image; the canvas takes ownership and disposes the previous one.</summary>
    public void SetImage(Image? image)
    {
        StopAnimation();
        Image? previous = _image;
        _image = image;
        _pan = PointF.Empty;
        CancelCrop();
        if (previous is not null && !ReferenceEquals(previous, image))
            previous.Dispose();

        if (_image is not null)
        {
            FitToWindow();
            StartAnimationIfNeeded();
        }
        Invalidate();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- Zoom / fit ---------------------------------------------------------

    /// <summary>Scales the image to fit the window, never enlarging beyond 100%.</summary>
    public void FitToWindow()
    {
        if (_image is null || ClientSize.Width <= 0 || ClientSize.Height <= 0)
            return;

        float fit = Math.Min(
            (float)ClientSize.Width / _image.Width,
            (float)ClientSize.Height / _image.Height);
        _zoom = Math.Min(fit, 1f);
        _pan = PointF.Empty;
        Invalidate();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ZoomToActualSize()
    {
        if (_image is null) return;
        _zoom = 1f;
        _pan = PointF.Empty;
        Invalidate();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ZoomBy(float factor, Point center)
    {
        if (_image is null) return;
        float newZoom = Math.Clamp(_zoom * factor, 0.02f, 40f);
        if (Math.Abs(newZoom - _zoom) < 1e-4f) return;

        // Keep the point under the cursor anchored while zooming.
        RectangleF before = GetImageRect();
        float relX = before.Width > 0 ? (center.X - before.X) / before.Width : 0.5f;
        float relY = before.Height > 0 ? (center.Y - before.Y) / before.Height : 0.5f;

        _zoom = newZoom;
        RectangleF after = GetImageRect();
        float anchorX = after.X + relX * after.Width;
        float anchorY = after.Y + relY * after.Height;
        _pan = new PointF(_pan.X + (center.X - anchorX), _pan.Y + (center.Y - anchorY));

        Invalidate();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The destination rectangle of the image in client coordinates.</summary>
    private RectangleF GetImageRect()
    {
        if (_image is null) return RectangleF.Empty;
        float w = _image.Width * _zoom;
        float h = _image.Height * _zoom;
        float x = (ClientSize.Width - w) / 2f + _pan.X;
        float y = (ClientSize.Height - h) / 2f + _pan.Y;
        return new RectangleF(x, y, w, h);
    }

    // ---- Crop ---------------------------------------------------------------

    public void BeginCrop()
    {
        if (_image is null) return;
        _cropMode = true;
        _selectionClient = Rectangle.Empty;
        Cursor = Cursors.Cross;
        Invalidate();
    }

    public void CancelCrop()
    {
        _cropMode = false;
        _dragging = false;
        _selectionClient = Rectangle.Empty;
        Cursor = Cursors.Default;
        Invalidate();
    }

    /// <summary>Returns the current crop selection in image pixel coordinates, or null.</summary>
    public Rectangle? GetCropRectangleInImage()
    {
        if (_image is null || _selectionClient.Width <= 1 || _selectionClient.Height <= 1)
            return null;

        RectangleF img = GetImageRect();
        if (img.Width <= 0 || img.Height <= 0) return null;

        float sx = (_selectionClient.Left - img.X) / _zoom;
        float sy = (_selectionClient.Top - img.Y) / _zoom;
        float sw = _selectionClient.Width / _zoom;
        float sh = _selectionClient.Height / _zoom;

        var rect = Rectangle.Round(new RectangleF(sx, sy, sw, sh));
        rect.Intersect(new Rectangle(0, 0, _image.Width, _image.Height));
        return rect.Width >= 1 && rect.Height >= 1 ? rect : null;
    }

    // ---- Input --------------------------------------------------------------

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (_image is null || e.Button != MouseButtons.Left) return;

        if (_cropMode)
        {
            _dragging = true;
            _dragStart = e.Location;
            _selectionClient = new Rectangle(e.Location, Size.Empty);
        }
        else
        {
            _panning = true;
            _panStart = e.Location;
            _panOrigin = _pan;
            Cursor = Cursors.SizeAll;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            int x = Math.Min(_dragStart.X, e.X);
            int y = Math.Min(_dragStart.Y, e.Y);
            int w = Math.Abs(e.X - _dragStart.X);
            int h = Math.Abs(e.Y - _dragStart.Y);
            _selectionClient = new Rectangle(x, y, w, h);
            Invalidate();
        }
        else if (_panning)
        {
            _pan = new PointF(_panOrigin.X + (e.X - _panStart.X), _panOrigin.Y + (e.Y - _panStart.Y));
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_panning)
        {
            _panning = false;
            Cursor = _cropMode ? Cursors.Cross : Cursors.Default;
        }
        _dragging = false;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_image is null) return;
        float factor = e.Delta > 0 ? 1.15f : 1f / 1.15f;
        ZoomBy(factor, e.Location);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (_cropMode) return;
        if (_zoom < 1f) ZoomToActualSize();
        else FitToWindow();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }

    // ---- Animation ----------------------------------------------------------

    private void StartAnimationIfNeeded()
    {
        if (_image is not null && ImageAnimator.CanAnimate(_image))
        {
            ImageAnimator.Animate(_image, OnFrameChanged);
            _animating = true;
        }
    }

    private void StopAnimation()
    {
        if (_animating && _image is not null)
            ImageAnimator.StopAnimate(_image, OnFrameChanged);
        _animating = false;
    }

    private void OnFrameChanged(object? sender, EventArgs e)
    {
        if (IsHandleCreated && !IsDisposed)
        {
            try { BeginInvoke(Invalidate); } catch { /* form closing */ }
        }
    }

    // ---- Painting -----------------------------------------------------------

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        // Painted in OnPaint instead to avoid flicker.
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(BackColor);

        if (_image is null)
        {
            TextRenderer.DrawText(g, "Drop an image here or use File ▸ Open",
                Font, ClientRectangle, Color.Gainsboro,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        if (_animating)
            ImageAnimator.UpdateFrames(_image);

        RectangleF dest = GetImageRect();

        // Checkerboard only behind the image so transparency reads clearly.
        EnsureChecker();
        Region old = g.Clip;
        g.SetClip(dest);
        g.FillRectangle(_checker!, dest);
        g.Clip = old;

        g.InterpolationMode = _zoom >= 1f ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingMode = CompositingMode.SourceOver;
        g.DrawImage(_image, dest);

        if (_cropMode)
            DrawCropOverlay(g, dest);
    }

    private void DrawCropOverlay(Graphics g, RectangleF imageRect)
    {
        using var shade = new SolidBrush(Color.FromArgb(120, 0, 0, 0));
        if (_selectionClient.Width <= 1 || _selectionClient.Height <= 1)
        {
            g.FillRectangle(shade, ClientRectangle);
            return;
        }

        // Dim everything outside the selection.
        var sel = _selectionClient;
        Region outside = new Region(ClientRectangle);
        outside.Exclude(sel);
        g.FillRegion(shade, outside);
        outside.Dispose();

        using var pen = new Pen(Color.White, 1.5f) { DashStyle = DashStyle.Dash };
        g.DrawRectangle(pen, sel);

        string label = $"{sel.Width} × {sel.Height} px (screen)";
        Rectangle? imgRect = GetCropRectangleInImage();
        if (imgRect is { } r)
            label = $"{r.Width} × {r.Height} px";
        TextRenderer.DrawText(g, label, Font,
            new Point(sel.Left + 4, Math.Max(0, sel.Top - 18)), Color.White);
    }

    private void EnsureChecker()
    {
        if (_checker is not null) return;
        const int cell = 8;
        var tile = new Bitmap(cell * 2, cell * 2);
        using (Graphics tg = Graphics.FromImage(tile))
        {
            Color light = Color.FromArgb(220, 220, 220);
            Color dark = Color.FromArgb(176, 176, 176);
            using var lb = new SolidBrush(light);
            using var db = new SolidBrush(dark);
            tg.FillRectangle(lb, 0, 0, cell * 2, cell * 2);
            tg.FillRectangle(db, 0, 0, cell, cell);
            tg.FillRectangle(db, cell, cell, cell, cell);
        }
        _checker = new TextureBrush(tile, WrapMode.Tile);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopAnimation();
            _checker?.Dispose();
            _image?.Dispose();
        }
        base.Dispose(disposing);
    }
}
