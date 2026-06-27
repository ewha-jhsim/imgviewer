using System;
using System.Drawing;
using System.Windows.Forms;

namespace ImgViewer.UI;

/// <summary>Small modeless dialog showing download progress during a self-update.</summary>
public sealed class UpdateProgressForm : Form
{
    private readonly ProgressBar _bar = new()
    {
        Minimum = 0,
        Maximum = 100,
        Style = ProgressBarStyle.Continuous,
        Dock = DockStyle.Bottom,
        Height = 24,
    };

    public UpdateProgressForm()
    {
        Text = "Downloading update…";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ControlBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(320, 70);

        var label = new Label
        {
            Text = "Downloading the new version, please wait…",
            Dock = DockStyle.Top,
            TextAlign = ContentAlignment.MiddleCenter,
            Height = 40,
        };
        Controls.Add(label);
        Controls.Add(_bar);
    }

    public void SetProgress(double fraction)
    {
        int value = Math.Clamp((int)Math.Round(fraction * 100), 0, 100);
        if (InvokeRequired)
            BeginInvoke(() => _bar.Value = value);
        else
            _bar.Value = value;
    }
}
