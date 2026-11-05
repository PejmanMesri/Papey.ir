using System.Drawing.Printing;
using Microsoft.Win32;

namespace Papey.Printer;

internal sealed class SetupForm : Form
{
    public Settings? Result { get; private set; }
    private readonly TextBox _username = new() { Text = "Admin", Dock = DockStyle.Fill };
    private readonly TextBox _password = new() { UseSystemPasswordChar = true, Dock = DockStyle.Fill };
    private readonly ComboBox _printer = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox _mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly Button _save = new() { Text = "اتصال و فعال‌سازی", AutoSize = true, Padding = new Padding(12, 6, 12, 6) };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(410, 0) };

    public SetupForm()
    {
        Text = "Papey Order Printer — راه‌اندازی"; ClientSize = new Size(470, 440);
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen; RightToLeft = RightToLeft.Yes;
        Font = new Font("Tahoma", 10);
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 12 };
        panel.Controls.Add(new Label { Text = "چاپ خودکار سفارش‌های پرداخت‌شده پاپی", AutoSize = true });
        panel.Controls.Add(new Label { Text = "نام کاربری مدیر فروشگاه", AutoSize = true }); panel.Controls.Add(_username);
        panel.Controls.Add(new Label { Text = "رمز عبور مدیر فروشگاه", AutoSize = true }); panel.Controls.Add(_password);
        panel.Controls.Add(new Label { Text = "چاپگر فاکتور", AutoSize = true }); panel.Controls.Add(_printer);
        foreach (string name in PrinterSettings.InstalledPrinters) _printer.Items.Add(name);
        var preferred = _printer.Items.Cast<string>().FirstOrDefault(x => x.Contains("BIXOLON", StringComparison.OrdinalIgnoreCase));
        if (preferred is not null) _printer.SelectedItem = preferred;
        else if (_printer.Items.Count > 0) _printer.SelectedIndex = 0;
        panel.Controls.Add(new Label { Text = "روش چاپ (اگر کاغذ سفید است، Windows driver را انتخاب کنید)", AutoSize = true });
        _mode.Items.AddRange(["Windows driver — سازگار با درایور چاپگر", "RAW ESC/POS — چاپ مستقیم و کوتاه"]);
        _mode.SelectedIndex = Store.Read<Settings>("settings.json")?.PrintMode == "raw" ? 1 : 0;
        panel.Controls.Add(_mode);
        panel.Controls.Add(_save); panel.Controls.Add(_status);
        Controls.Add(panel); AcceptButton = _save;
        _save.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_username.Text) || _password.Text.Length == 0 || _printer.SelectedItem is not string printer)
            { _status.Text = "نام کاربری، رمز عبور و چاپگر را انتخاب کنید."; return; }
            _save.Enabled = false; _status.Text = "در حال بررسی اتصال به papey.ir…";
            var old = Store.Read<Settings>("settings.json");
            var settings = new Settings("https://papey.ir/", printer, _username.Text.Trim(), Store.Protect(_password.Text),
                old?.ClientId ?? "papey-" + Guid.NewGuid().ToString("N"), _mode.SelectedIndex == 0 ? "driver" : "raw");
            try
            {
                using var api = new PrintingApi(settings); await api.Connect();
                Store.Write("settings.json", settings);
                using var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                run.SetValue("Papey Order Printer", "\"" + Application.ExecutablePath + "\"");
                _password.Clear(); Result = settings; DialogResult = DialogResult.OK; Close();
            }
            catch { _status.Text = "اتصال انجام نشد. اینترنت و اطلاعات حساب مدیر را بررسی کنید؛ پس از یک دقیقه دوباره تلاش کنید."; }
            finally { _save.Enabled = true; }
        };
    }
}
