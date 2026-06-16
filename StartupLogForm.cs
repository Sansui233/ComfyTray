using System.Drawing;
using System.Windows.Forms;

sealed class StartupLogForm : Form
{
    private readonly TextBox _logTextBox;

    public StartupLogForm()
    {
        Text = "ComfyUI Starting";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(760, 420);
        MinimumSize = new Size(480, 260);
        MinimizeBox = false;
        MaximizeBox = true;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = false;

        _logTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(18, 18, 18),
            ForeColor = Color.FromArgb(230, 230, 230),
            BorderStyle = BorderStyle.None,
            Font = new Font("Consolas", 9F),
            WordWrap = false
        };

        Controls.Add(_logTextBox);
    }

    public void AppendLine(string line)
    {
        if (_logTextBox.TextLength > 200_000)
        {
            _logTextBox.Clear();
        }

        _logTextBox.AppendText(line + Environment.NewLine);
    }

    public void Clear()
    {
        _logTextBox.Clear();
    }

    public void BeginInvokeIfNeeded(Action action)
    {
        if (IsDisposed) return;

        if (InvokeRequired)
        {
            BeginInvoke(action);
            return;
        }

        action();
    }
}
