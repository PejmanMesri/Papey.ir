namespace Papey.Printer;

internal sealed class PrinterDashboard : Form
{
    public PrinterDashboard(Func<string> status, IReadOnlyList<ToolStripItem> commands)
    {
        Text = "Papey Order Printer";
        Size = new Size(820, 520);
        MinimumSize = new Size(480, 320);
        StartPosition = FormStartPosition.CenterScreen;

        var summary = new Label { Dock = DockStyle.Top, Height = 75, Padding = new Padding(12) };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 92, AutoScroll = true, Padding = new Padding(8) };
        var log = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false };
        Controls.Add(log);
        Controls.Add(actions);
        Controls.Add(summary);

        foreach (var command in commands)
        {
            var button = new Button { AutoSize = true, Text = command.Text };
            button.Click += (_, _) => command.PerformClick();
            command.TextChanged += (_, _) => { if (!button.IsDisposed) button.Text = command.Text; };
            actions.Controls.Add(button);
        }

        var copy = new Button { AutoSize = true, Text = "Copy recent log" };
        copy.Click += (_, _) => { if (log.TextLength > 0) Clipboard.SetText(log.Text); };
        actions.Controls.Add(copy);

        void RefreshStatus()
        {
            summary.Text = status();
            string content;
            try { content = string.Join(Environment.NewLine, File.ReadLines(Store.PathFor("agent.log")).TakeLast(150)); }
            catch (IOException) { return; }
            if (log.Text == content) return;
            log.Text = content;
            log.SelectionStart = log.TextLength;
            log.ScrollToCaret();
        }

        var refresh = new System.Windows.Forms.Timer { Interval = 2000 };
        refresh.Tick += (_, _) => RefreshStatus();
        FormClosed += (_, _) => refresh.Dispose();
        RefreshStatus();
        refresh.Start();
    }
}
