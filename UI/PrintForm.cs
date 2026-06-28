using System;
using System.Drawing;
using System.Drawing.Printing;
using System.Windows.Forms;

namespace ImgViewer.UI;

/// <summary>
/// A compact print dialog with an embedded single-page preview plus the options that
/// matter for one image: printer, orientation (portrait/landscape) and copies.
/// Deliberately omits the stock PrintPreviewDialog's multi-page and zoom controls.
/// </summary>
public sealed class PrintForm : Form
{
    private readonly PrintDocument _doc;

    private readonly PrintPreviewControl _preview = new()
    {
        AutoZoom = true,
        Columns = 1,
        Rows = 1,
        UseAntiAlias = true,
        Dock = DockStyle.Fill,
    };
    private readonly ComboBox _printer = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };
    private readonly RadioButton _portrait = new() { Text = "Portrait", AutoSize = true, Checked = true };
    private readonly RadioButton _landscape = new() { Text = "Landscape", AutoSize = true };
    private readonly NumericUpDown _copies = new() { Minimum = 1, Maximum = 99, Value = 1, Width = 60 };

    public PrintForm(PrintDocument doc)
    {
        _doc = doc;

        Text = "Print";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(780, 640);
        MinimumSize = new Size(560, 480);
        ShowInTaskbar = false;

        foreach (string name in PrinterSettings.InstalledPrinters)
            _printer.Items.Add(name);
        if (_printer.Items.Contains(_doc.PrinterSettings.PrinterName))
            _printer.SelectedItem = _doc.PrinterSettings.PrinterName;
        else if (_printer.Items.Count > 0)
            _printer.SelectedIndex = 0;

        _landscape.Checked = _doc.DefaultPageSettings.Landscape;
        _portrait.Checked = !_doc.DefaultPageSettings.Landscape;

        var orientation = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0), WrapContents = false };
        orientation.Controls.Add(_portrait);
        orientation.Controls.Add(_landscape);

        var options = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(10, 8, 10, 8),
            AutoSize = true,
            WrapContents = true,
        };
        options.Controls.Add(MakeLabel("Printer:"));
        options.Controls.Add(_printer);
        options.Controls.Add(MakeLabel("Orientation:", 16));
        options.Controls.Add(orientation);
        options.Controls.Add(MakeLabel("Copies:", 16));
        options.Controls.Add(_copies);

        var printBtn = new Button { Text = "Print", Width = 90, Anchor = AnchorStyles.Right };
        var cancelBtn = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90, Anchor = AnchorStyles.Right };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 44, Padding = new Padding(8) };
        buttons.Controls.Add(cancelBtn);
        buttons.Controls.Add(printBtn);

        _preview.Document = _doc;

        Controls.Add(_preview);   // fill (added first so it ends up behind the docked bars)
        Controls.Add(options);    // top
        Controls.Add(buttons);    // bottom

        AcceptButton = printBtn;
        CancelButton = cancelBtn;

        _printer.SelectedIndexChanged += (_, _) => { ApplyPrinter(); RefreshPreview(); };
        _portrait.CheckedChanged += (_, _) => ApplyOrientation();
        _landscape.CheckedChanged += (_, _) => ApplyOrientation();
        printBtn.Click += (_, _) => DoPrint();

        if (_printer.Items.Count == 0)
            printBtn.Enabled = false;
        else
            ApplyPrinter();
    }

    private static Label MakeLabel(string text, int leftMargin = 0) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(leftMargin, 6, 4, 0),
    };

    private void ApplyPrinter()
    {
        if (_printer.SelectedItem is string name)
            _doc.PrinterSettings.PrinterName = name;
    }

    private void ApplyOrientation()
    {
        _doc.DefaultPageSettings.Landscape = _landscape.Checked;
        RefreshPreview();
    }

    private void RefreshPreview() => _preview.InvalidatePreview();

    private void DoPrint()
    {
        try
        {
            ApplyPrinter();
            _doc.DefaultPageSettings.Landscape = _landscape.Checked;
            _doc.PrinterSettings.Copies = (short)_copies.Value;
            _doc.Print();
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not print:\n{ex.Message}",
                "Print", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
