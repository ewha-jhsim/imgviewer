using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ImgViewer.Services;

namespace ImgViewer.UI;

public sealed class MainForm : Form
{
    private readonly ImageCanvas _canvas = new() { Dock = DockStyle.Fill };
    private readonly FolderNavigator _navigator = new();
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _statusName = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _statusInfo = new();
    private readonly ToolStripStatusLabel _statusPos = new();

    // Action bar shown only while cropping (gives a clickable Apply/Cancel).
    // Uses the default (light) ToolStrip rendering so its text stays readable.
    private readonly ToolStrip _cropBar = new()
    {
        Visible = false,
        Dock = DockStyle.Top,
        GripStyle = ToolStripGripStyle.Hidden,
    };

    private string? _currentPath;
    private bool _dirty;

    // Edit history (resize/crop). Each list holds independent bitmap snapshots that this
    // form owns and disposes — the canvas owns only the live image it is showing.
    private const int MaxHistory = 20;
    private readonly List<Bitmap> _undo = new();
    private readonly List<Bitmap> _redo = new();
    private ToolStripMenuItem? _undoItem;
    private ToolStripMenuItem? _redoItem;

    // Fullscreen bookkeeping.
    private FormWindowState _prevState;
    private FormBorderStyle _prevBorder;
    private bool _fullscreen;

    public MainForm(string? initialPath)
    {
        Text = "ImgViewer";
        ClientSize = new Size(1000, 700);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        AllowDrop = true;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(32, 32, 32);

        // Reuse the .exe's embedded application icon for the window/taskbar.
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!); }
        catch { /* leave the default icon if extraction fails */ }

        _canvas.ViewChanged += (_, _) => UpdateStatus();
        Controls.Add(_canvas);
        BuildCropBar();
        Controls.Add(_cropBar);   // added before the menu so the menu stays on top
        Controls.Add(BuildMenu());
        BuildStatusBar();

        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;

        Load += async (_, _) =>
        {
            if (initialPath is not null)
                OpenPath(initialPath);
            else
                UpdateStatus();

            // Silent background update check on launch; only prompts if a newer build
            // is actually published (failures are swallowed when not interactive).
            // Skipped until a real update feed is configured in Services/Updater.cs.
            if (Updater.IsConfigured)
                await CheckForUpdatesAsync(interactive: false);
        };
    }

    // ---- UI construction ----------------------------------------------------

    private static ToolStripMenuItem Item(
        string text, EventHandler handler, Keys shortcut = Keys.None, string? shortcutText = null)
    {
        var item = new ToolStripMenuItem(text, null, handler);
        // Note: only modifier combos, function keys, Delete and Insert are valid
        // ShortcutKeys. Bare arrow keys would throw, so they use shortcutText (display
        // only) and are handled in ProcessCmdKey instead.
        if (shortcut != Keys.None)
            item.ShortcutKeys = shortcut;
        if (shortcutText is not null)
            item.ShortcutKeyDisplayString = shortcutText;
        return item;
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip();

        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add(Item("&Open…", (_, _) => OpenWithDialog(), Keys.Control | Keys.O));
        file.DropDownItems.Add(Item("&Save", (_, _) => Save(), Keys.Control | Keys.S));
        file.DropDownItems.Add(Item("Save &As…", (_, _) => SaveAs(), Keys.Control | Keys.Shift | Keys.S));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item("&Print…", (_, _) => PrintImage(), Keys.Control | Keys.P));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item("&Delete file", (_, _) => DeleteCurrent(), Keys.Delete));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item("E&xit", (_, _) => Close()));
        menu.Items.Add(file);

        var view = new ToolStripMenuItem("&View");
        view.DropDownItems.Add(Item("&Next image", (_, _) => Navigate(+1), shortcutText: "→"));
        view.DropDownItems.Add(Item("&Previous image", (_, _) => Navigate(-1), shortcutText: "←"));
        view.DropDownItems.Add(new ToolStripSeparator());
        view.DropDownItems.Add(Item("Zoom &In", (_, _) => _canvas.ZoomBy(1.25f, CanvasCenter()), Keys.Control | Keys.Oemplus));
        view.DropDownItems.Add(Item("Zoom &Out", (_, _) => _canvas.ZoomBy(0.8f, CanvasCenter()), Keys.Control | Keys.OemMinus));
        view.DropDownItems.Add(Item("&Fit to window", (_, _) => _canvas.FitToWindow(), Keys.Control | Keys.D0));
        view.DropDownItems.Add(Item("Actual si&ze (100%)", (_, _) => _canvas.ZoomToActualSize(), Keys.Control | Keys.D1));
        view.DropDownItems.Add(Item("&Fullscreen", (_, _) => ToggleFullscreen(), Keys.F11));
        menu.Items.Add(view);

        var edit = new ToolStripMenuItem("&Edit");
        _undoItem = Item("&Undo", (_, _) => Undo(), Keys.Control | Keys.Z);
        _redoItem = Item("&Redo", (_, _) => Redo(), Keys.Control | Keys.Y);
        _undoItem.Enabled = false;
        _redoItem.Enabled = false;
        edit.DropDownItems.Add(_undoItem);
        edit.DropDownItems.Add(_redoItem);
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add(Item("&Resize…", (_, _) => ResizeImage()));
        edit.DropDownItems.Add(Item("&Crop", (_, _) => StartCrop(), Keys.Control | Keys.R));
        menu.Items.Add(edit);

        var tools = new ToolStripMenuItem("&Tools");
        tools.DropDownItems.Add(Item("Set as &default image viewer…", (_, _) => SetAsDefault()));
        tools.DropDownItems.Add(Item("&Remove file associations", (_, _) => RemoveAssociations()));
        menu.Items.Add(tools);

        var help = new ToolStripMenuItem("&Help");
        help.DropDownItems.Add(Item("&Check for updates…", async (_, _) => await CheckForUpdatesAsync(interactive: true)));
        help.DropDownItems.Add(Item("&About", (_, _) => ShowAbout()));
        menu.Items.Add(help);

        MainMenuStrip = menu;
        return menu;
    }

    private void BuildStatusBar()
    {
        _status.Items.AddRange(new ToolStripItem[] { _statusName, _statusInfo, _statusPos });
        _status.SizingGrip = false;
        // Force readable colours (the default dark form theme made the text invisible).
        _status.BackColor = Color.FromArgb(45, 45, 48);
        _status.ForeColor = Color.Gainsboro;
        foreach (ToolStripStatusLabel label in new[] { _statusName, _statusInfo, _statusPos })
            label.ForeColor = Color.Gainsboro;
        Controls.Add(_status);
    }

    private Point CanvasCenter() => new(_canvas.ClientSize.Width / 2, _canvas.ClientSize.Height / 2);

    // ---- Open / navigate ----------------------------------------------------

    private void OpenWithDialog()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Open image",
            Filter = "Images|" + string.Join(";", Array.ConvertAll(ImageLoader.SupportedExtensions, e => "*" + e)) +
                     "|All files|*.*",
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            OpenPath(dlg.FileName);
    }

    private void OpenPath(string path)
    {
        if (!ConfirmSaveIfDirty())
            return;
        try
        {
            Image image = ImageLoader.Load(path);
            _canvas.SetImage(image);
            _currentPath = path;
            _dirty = false;
            _navigator.Load(path);
            ResetHistory();
            UpdateStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open:\n{path}\n\n{ex.Message}",
                "ImgViewer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void Navigate(int delta)
    {
        if (_canvas.CropMode) { CancelCrop(); return; }
        if (_navigator.Count == 0) return;
        if (!ConfirmSaveIfDirty()) return;

        string? next = delta > 0 ? _navigator.MoveNext() : _navigator.MovePrevious();
        if (next is null) return;

        try
        {
            Image image = ImageLoader.Load(next);
            _canvas.SetImage(image);
            _currentPath = next;
            _dirty = false;
            ResetHistory();
            UpdateStatus();
        }
        catch (Exception ex)
        {
            _statusInfo.Text = $"Failed: {ex.Message}";
        }
    }

    // ---- Editing ------------------------------------------------------------

    private Bitmap? SnapshotCurrent()
    {
        if (_canvas.CurrentImage is not { } src) return null;
        var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
        using Graphics g = Graphics.FromImage(bmp);
        g.DrawImageUnscaled(src, 0, 0);
        return bmp;
    }

    private void ResizeImage()
    {
        if (!_canvas.HasImage) return;
        using var dlg = new ResizeDialog(_canvas.ImageSize);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        Image src = _canvas.CurrentImage!;
        var resized = new Bitmap(dlg.NewWidth, dlg.NewHeight, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(resized))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.DrawImage(src, new Rectangle(0, 0, dlg.NewWidth, dlg.NewHeight));
        }
        ApplyEdit(resized);
    }

    // ---- Edit history (undo/redo) ------------------------------------------

    /// <summary>Replaces the live image with an edited one, recording an undo step.</summary>
    private void ApplyEdit(Bitmap edited)
    {
        Bitmap? before = SnapshotCurrent();   // independent copy of the pre-edit image
        if (before is not null)
            PushHistory(_undo, before);
        ClearHistory(_redo);

        _canvas.SetImage(edited);   // canvas disposes the old live image (not our snapshot)
        _dirty = true;
        UpdateEditMenu();
        UpdateStatus();
    }

    private void Undo()
    {
        if (_undo.Count == 0) return;
        Bitmap? current = SnapshotCurrent();
        if (current is not null)
            PushHistory(_redo, current);

        Bitmap restore = Pop(_undo);
        _canvas.SetImage(restore);  // ownership transfers to the canvas
        _dirty = true;
        UpdateEditMenu();
        UpdateStatus();
    }

    private void Redo()
    {
        if (_redo.Count == 0) return;
        Bitmap? current = SnapshotCurrent();
        if (current is not null)
            PushHistory(_undo, current);

        Bitmap restore = Pop(_redo);
        _canvas.SetImage(restore);
        _dirty = true;
        UpdateEditMenu();
        UpdateStatus();
    }

    private static void PushHistory(List<Bitmap> stack, Bitmap bmp)
    {
        stack.Add(bmp);
        if (stack.Count > MaxHistory)
        {
            stack[0].Dispose();     // drop the oldest step
            stack.RemoveAt(0);
        }
    }

    private static Bitmap Pop(List<Bitmap> stack)
    {
        Bitmap bmp = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        return bmp;
    }

    private static void ClearHistory(List<Bitmap> stack)
    {
        foreach (Bitmap b in stack)
            b.Dispose();
        stack.Clear();
    }

    /// <summary>Discards all undo/redo history (e.g. when a different image is opened).</summary>
    private void ResetHistory()
    {
        ClearHistory(_undo);
        ClearHistory(_redo);
        UpdateEditMenu();
    }

    private void UpdateEditMenu()
    {
        if (_undoItem is not null) _undoItem.Enabled = _undo.Count > 0;
        if (_redoItem is not null) _redoItem.Enabled = _redo.Count > 0;
    }

    private void BuildCropBar()
    {
        var hint = new ToolStripLabel("Crop — drag a region, then:");
        var apply = new ToolStripButton("✓ Apply (Enter)")
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
        };
        apply.Click += (_, _) => ApplyCrop();
        var cancel = new ToolStripButton("✕ Cancel (Esc)")
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
        };
        cancel.Click += (_, _) => CancelCrop();

        _cropBar.Items.Add(hint);
        _cropBar.Items.Add(new ToolStripSeparator());
        _cropBar.Items.Add(apply);
        _cropBar.Items.Add(cancel);
    }

    private void StartCrop()
    {
        if (!_canvas.HasImage) return;
        _canvas.BeginCrop();
        _cropBar.Visible = true;
        _statusInfo.Text = "Crop: drag a region, then click Apply (or press Enter).";
    }

    private void ApplyCrop()
    {
        if (_canvas.GetCropRectangleInImage() is not { } rect)
        {
            // No usable selection yet — stay in crop mode so the user can drag one.
            _statusInfo.Text = "Drag a region to crop first, then Apply.";
            return;
        }

        Image src = _canvas.CurrentImage!;
        var cropped = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(cropped))
            g.DrawImage(src, new Rectangle(0, 0, rect.Width, rect.Height), rect, GraphicsUnit.Pixel);

        _cropBar.Visible = false;
        ApplyEdit(cropped);   // records undo and cancels the canvas crop mode via SetImage
    }

    private void CancelCrop()
    {
        _cropBar.Visible = false;
        _canvas.CancelCrop();
        UpdateStatus();
    }

    // ---- Save / delete ------------------------------------------------------

    /// <summary>Saves the current image. Returns true only if a save actually completed.</summary>
    private bool Save()
    {
        if (_currentPath is null || !ImageSaver.CanSave(_currentPath))
            return SaveAs();
        return SaveTo(_currentPath);
    }

    private bool SaveAs()
    {
        if (!_canvas.HasImage) return false;
        using var dlg = new SaveFileDialog
        {
            Title = "Save image as",
            Filter = "PNG (*.png)|*.png|JPEG (*.jpg)|*.jpg|WebP (*.webp)|*.webp|BMP (*.bmp)|*.bmp|GIF (*.gif)|*.gif|TIFF (*.tif)|*.tif",
            FileName = _currentPath is null ? "image.png" : Path.GetFileName(_currentPath),
        };
        return dlg.ShowDialog(this) == DialogResult.OK && SaveTo(dlg.FileName);
    }

    private bool SaveTo(string path)
    {
        using Bitmap? snapshot = SnapshotCurrent();
        if (snapshot is null) return false;
        try
        {
            ImageSaver.Save(snapshot, path);
            _currentPath = path;
            _dirty = false;
            _navigator.Refresh();
            UpdateStatus();
            _statusInfo.Text = "Saved";
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not save:\n{ex.Message}",
                "ImgViewer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    // ---- Printing -----------------------------------------------------------

    private PrintDocument CreatePrintDocument(Image image)
    {
        var doc = new PrintDocument
        {
            DocumentName = _currentPath is null ? "ImgViewer image" : Path.GetFileName(_currentPath),
        };
        doc.PrintPage += (_, e) => DrawForPrint(e, image);
        return doc;
    }

    /// <summary>Scales the image to fit the printable area (margins), centered, aspect kept.</summary>
    private static void DrawForPrint(PrintPageEventArgs e, Image image)
    {
        Graphics g = e.Graphics!;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        Rectangle area = e.MarginBounds;
        double scale = Math.Min((double)area.Width / image.Width, (double)area.Height / image.Height);
        int w = Math.Max(1, (int)Math.Round(image.Width * scale));
        int h = Math.Max(1, (int)Math.Round(image.Height * scale));
        int x = area.X + (area.Width - w) / 2;
        int y = area.Y + (area.Height - h) / 2;

        g.DrawImage(image, new Rectangle(x, y, w, h));
        e.HasMorePages = false;
    }

    private void PrintImage()
    {
        if (!_canvas.HasImage) return;
        // Snapshot stays alive for the whole lifetime of the print dialog (preview renders
        // repeatedly and Print() may be invoked more than once before it closes).
        using Bitmap? snapshot = SnapshotCurrent();
        if (snapshot is null) return;

        using PrintDocument doc = CreatePrintDocument(snapshot);
        using var dlg = new PrintForm(doc) { Icon = Icon };
        dlg.ShowDialog(this);
    }

    private void DeleteCurrent()
    {
        if (_currentPath is null || !File.Exists(_currentPath)) return;
        if (MessageBox.Show(this, $"Delete this file?\n\n{_currentPath}", "ImgViewer",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        try
        {
            // Send to Recycle Bin rather than hard-deleting.
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                _currentPath,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not delete:\n{ex.Message}", "ImgViewer",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string? next = _navigator.RemoveCurrent();
        _dirty = false;
        if (next is null)
        {
            _canvas.SetImage(null);
            _currentPath = null;
        }
        else
        {
            _currentPath = next;
            try { _canvas.SetImage(ImageLoader.Load(next)); } catch { _canvas.SetImage(null); }
        }
        ResetHistory();
        UpdateStatus();
    }

    // ---- Default app / updates ---------------------------------------------

    private void SetAsDefault()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (DefaultApp.Register(machineWide: false))
        {
            MessageBox.Show(this,
                "ImgViewer is now registered as an image handler.\n\n" +
                "Windows will open the Default Apps page — choose ImgViewer for the " +
                "image types you want it to handle.",
                "ImgViewer", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DefaultApp.OpenDefaultAppsSettings();
        }
        else
        {
            MessageBox.Show(this, "Could not register file associations.",
                "ImgViewer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void RemoveAssociations()
    {
        if (!OperatingSystem.IsWindows()) return;
        DefaultApp.Unregister(machineWide: false);
        MessageBox.Show(this, "File associations removed.", "ImgViewer",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task CheckForUpdatesAsync(bool interactive)
    {
        if (!Updater.IsConfigured)
        {
            if (interactive)
                MessageBox.Show(this,
                    "No update source is configured.\n\nSet GitHubOwner / GitHubRepo in " +
                    "Services/Updater.cs (or drop an 'update.url' file next to the exe).",
                    "ImgViewer", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            _statusInfo.Text = "Checking for updates…";
            UpdateInfo? info = await Updater.CheckForUpdateAsync();
            if (info is null)
            {
                _statusInfo.Text = "Up to date";
                if (interactive)
                    MessageBox.Show(this, $"You're on the latest version ({Updater.CurrentVersion}).",
                        "ImgViewer", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string msg = $"A new version is available: {info.Version}\n" +
                         $"(current {Updater.CurrentVersion})\n\n{info.Notes}\n\nDownload and install now?";
            if (MessageBox.Show(this, msg, "Update available",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
                return;

            using var progress = new UpdateProgressForm();
            progress.Show(this);
            var report = new Progress<double>(p => progress.SetProgress(p));
            string file = await Updater.DownloadAsync(info, report);
            progress.Close();

            Updater.ApplyAndRestart(file);
            Close(); // helper script relaunches the new build after we exit
        }
        catch (Exception ex)
        {
            _statusInfo.Text = "Update check failed";
            if (interactive)
                MessageBox.Show(this, $"Update failed:\n{ex.Message}",
                    "ImgViewer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ShowAbout()
    {
        MessageBox.Show(this,
            $"ImgViewer {Updater.CurrentVersion}\n\n" +
            "Lightweight image viewer for Windows.\n" +
            "Formats: PNG, JPG, GIF, BMP, WebP, TIFF, ICO.\n\n" +
            "Left/Right: previous/next image\n" +
            "Mouse wheel: zoom, drag: pan, double-click: fit/100%\n" +
            "Ctrl+0: fit, Ctrl+1: actual size, F11: fullscreen\n" +
            "Ctrl+P: print, Ctrl+Z/Ctrl+Y: undo/redo",
            "About ImgViewer", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ---- Fullscreen ---------------------------------------------------------

    private void ToggleFullscreen()
    {
        if (!_fullscreen)
        {
            _prevState = WindowState;
            _prevBorder = FormBorderStyle;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Normal; // ensure the change takes effect
            WindowState = FormWindowState.Maximized;
            MainMenuStrip!.Visible = false;
            _status.Visible = false;
            _fullscreen = true;
        }
        else
        {
            FormBorderStyle = _prevBorder;
            WindowState = _prevState;
            MainMenuStrip!.Visible = true;
            _status.Visible = true;
            _fullscreen = false;
        }
    }

    // ---- Keyboard -----------------------------------------------------------

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Right:
            case Keys.PageDown:
            case Keys.Space:
                Navigate(+1);
                return true;
            case Keys.Left:
            case Keys.PageUp:
                Navigate(-1);
                return true;
            case Keys.Enter:
                if (_canvas.CropMode) { ApplyCrop(); return true; }
                break;
            case Keys.Escape:
                if (_canvas.CropMode) { CancelCrop(); return true; }
                if (_fullscreen) { ToggleFullscreen(); return true; }
                break;
            case Keys.Oemplus:
            case Keys.Add:
                _canvas.ZoomBy(1.25f, CanvasCenter());
                return true;
            case Keys.OemMinus:
            case Keys.Subtract:
                _canvas.ZoomBy(0.8f, CanvasCenter());
                return true;
            case Keys.Control | Keys.Shift | Keys.Z:   // common alias for Redo
                Redo();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ---- Drag & drop --------------------------------------------------------

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            e.Effect = DragDropEffects.Copy;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            string first = Array.Find(files, ImageLoader.IsSupported) ?? files[0];
            OpenPath(first);
        }
    }

    // ---- Helpers ------------------------------------------------------------

    /// <summary>
    /// If there are unsaved edits, offers to save them. Returns true if the caller may
    /// proceed (saved or intentionally discarded) or false if the user cancelled.
    /// </summary>
    private bool ConfirmSaveIfDirty()
    {
        if (!_dirty) return true;

        string name = _currentPath is null ? "this image" : Path.GetFileName(_currentPath);
        DialogResult r = MessageBox.Show(this,
            $"Save changes to {name}?", "ImgViewer",
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);

        return r switch
        {
            DialogResult.Yes => Save(),   // proceed only if the save actually completes
            DialogResult.No => true,      // discard changes
            _ => false,                   // Cancel (or dialog closed) — abort
        };
    }

    private void UpdateStatus()
    {
        if (_currentPath is null && !_canvas.HasImage)
        {
            Text = "ImgViewer";
            _statusName.Text = "No image";
            _statusInfo.Text = string.Empty;
            _statusPos.Text = string.Empty;
            return;
        }

        string name = _currentPath is null ? "(unsaved)" : Path.GetFileName(_currentPath);
        Size sz = _canvas.ImageSize;
        string dirty = _dirty ? " ●" : string.Empty;

        Text = $"{name}{dirty} — {sz.Width}×{sz.Height} — ImgViewer";
        _statusName.Text = _currentPath ?? "(unsaved image)";
        _statusInfo.Text = $"{sz.Width} × {sz.Height} px   {(int)Math.Round(_canvas.Zoom * 100)}%";
        _statusPos.Text = _navigator.Count > 0 ? $"{_navigator.Position + 1} / {_navigator.Count}" : string.Empty;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_dirty && !ConfirmSaveIfDirty())
        {
            e.Cancel = true;
            base.OnFormClosing(e);
            return;
        }
        ResetHistory();   // dispose any retained snapshots
        base.OnFormClosing(e);
    }
}
