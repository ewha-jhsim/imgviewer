using System;
using System.Drawing;
using System.Windows.Forms;

namespace ImgViewer.UI;

/// <summary>Prompts for new pixel dimensions, optionally keeping the aspect ratio.</summary>
public sealed class ResizeDialog : Form
{
    private readonly NumericUpDown _width = new() { Minimum = 1, Maximum = 100000, Width = 90 };
    private readonly NumericUpDown _height = new() { Minimum = 1, Maximum = 100000, Width = 90 };
    private readonly CheckBox _lock = new() { Text = "Keep aspect ratio", Checked = true, AutoSize = true };

    private readonly double _ratio;
    private bool _syncing;

    public int NewWidth => (int)_width.Value;
    public int NewHeight => (int)_height.Value;

    public ResizeDialog(Size original)
    {
        _ratio = original.Height == 0 ? 1.0 : (double)original.Width / original.Height;
        _width.Value = Math.Clamp(original.Width, 1, 100000);
        _height.Value = Math.Clamp(original.Height, 1, 100000);

        Text = "Resize image";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(280, 150);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 2,
            RowCount = 4,
        };
        layout.Controls.Add(new Label { Text = "Width", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        layout.Controls.Add(_width, 1, 0);
        layout.Controls.Add(new Label { Text = "Height", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        layout.Controls.Add(_height, 1, 1);
        layout.Controls.Add(_lock, 0, 2);
        layout.SetColumnSpan(_lock, 2);

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Anchor = AnchorStyles.Right };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Anchor = AnchorStyles.Right };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        layout.Controls.Add(buttons, 0, 3);
        layout.SetColumnSpan(buttons, 2);

        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = cancel;

        _width.ValueChanged += (_, _) => SyncFromWidth();
        _height.ValueChanged += (_, _) => SyncFromHeight();
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
