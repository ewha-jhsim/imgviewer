using System;
using System.Drawing;
using System.Windows.Forms;

namespace ImgViewer.UI;

/// <summary>
/// Prompts for new dimensions either as absolute pixels (with optional aspect lock) or as
/// a percentage of the original size.
/// </summary>
public sealed class ResizeDialog : Form
{
    private readonly RadioButton _byPixels = new() { Text = "By pixels", Checked = true, AutoSize = true };
    private readonly RadioButton _byPercent = new() { Text = "By percent", AutoSize = true };

    private readonly NumericUpDown _width = new() { Minimum = 1, Maximum = 100000, Width = 90 };
    private readonly NumericUpDown _height = new() { Minimum = 1, Maximum = 100000, Width = 90 };
    private readonly CheckBox _lock = new() { Text = "Keep aspect ratio", Checked = true, AutoSize = true };
    private readonly NumericUpDown _percent = new() { Minimum = 1, Maximum = 2000, Value = 100, Width = 90, DecimalPlaces = 0 };

    private readonly Size _original;
    private readonly double _ratio;
    private bool _syncing;

    public int NewWidth => _byPercent.Checked
        ? Math.Max(1, (int)Math.Round(_original.Width * (double)_percent.Value / 100.0))
        : (int)_width.Value;

    public int NewHeight => _byPercent.Checked
        ? Math.Max(1, (int)Math.Round(_original.Height * (double)_percent.Value / 100.0))
        : (int)_height.Value;

    public ResizeDialog(Size original)
    {
        _original = original;
        _ratio = original.Height == 0 ? 1.0 : (double)original.Width / original.Height;
        _width.Value = Math.Clamp(original.Width, 1, 100000);
        _height.Value = Math.Clamp(original.Height, 1, 100000);

        Text = "Resize image";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(300, 210);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 2,
            RowCount = 6,
        };

        layout.Controls.Add(new Label { Text = $"Original: {original.Width} × {original.Height} px", AutoSize = true }, 0, 0);
        layout.SetColumnSpan(layout.GetControlFromPosition(0, 0)!, 2);

        layout.Controls.Add(_byPixels, 0, 1);
        layout.Controls.Add(_byPercent, 1, 1);

        layout.Controls.Add(new Label { Text = "Width", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        layout.Controls.Add(_width, 1, 2);
        layout.Controls.Add(new Label { Text = "Height", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
        layout.Controls.Add(_height, 1, 3);
        layout.Controls.Add(_lock, 0, 4);
        layout.SetColumnSpan(_lock, 2);

        // Percent input shares rows with width/height; toggled by the radio buttons.
        var percentLabel = new Label { Text = "Scale %", AutoSize = true, Anchor = AnchorStyles.Left };
        layout.Controls.Add(percentLabel, 0, 5);
        layout.Controls.Add(_percent, 1, 5);

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Anchor = AnchorStyles.Right };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Anchor = AnchorStyles.Right };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, Height = 36 };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        Controls.Add(layout);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;

        _width.ValueChanged += (_, _) => SyncFromWidth();
        _height.ValueChanged += (_, _) => SyncFromHeight();
        _byPixels.CheckedChanged += (_, _) => UpdateMode();
        _byPercent.CheckedChanged += (_, _) => UpdateMode();
        UpdateMode();
    }

    private void UpdateMode()
    {
        bool pixels = _byPixels.Checked;
        _width.Enabled = pixels;
        _height.Enabled = pixels;
        _lock.Enabled = pixels;
        _percent.Enabled = !pixels;
    }

    private void SyncFromWidth()
    {
        if (_syncing || !_lock.Checked) return;
        _syncing = true;
        _height.Value = Math.Clamp((decimal)Math.Round((double)_width.Value / _ratio), 1, 100000);
        _syncing = false;
    }

    private void SyncFromHeight()
    {
        if (_syncing || !_lock.Checked) return;
        _syncing = true;
        _width.Value = Math.Clamp((decimal)Math.Round((double)_height.Value * _ratio), 1, 100000);
        _syncing = false;
    }
}
