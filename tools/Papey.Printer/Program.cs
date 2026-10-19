using System.Drawing.Printing;

namespace Papey.Printer;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Directory.CreateDirectory(Store.Folder);
        try
        {
            if (args.Length == 3 && args[0] == "--configure")
            {
                // Provision from an existing local credential file, never a command-line password.
                var old = Store.Read<Settings>("settings.json");
                Store.Write("settings.json", new Settings("https://papey.ir/", args[2], "Admin",
                    Store.Protect(File.ReadAllText(args[1]).Trim()), old?.ClientId ?? "papey-" + Guid.NewGuid().ToString("N")));
                Store.Status("Configured for papey.ir and " + args[2]); return;
            }
            var settings = Store.Read<Settings>("settings.json") ?? throw new InvalidOperationException("Run the printer setup first.");
            if (!PrinterSettings.InstalledPrinters.Cast<string>().Contains(settings.Printer))
                throw new InvalidOperationException("Configured printer is not installed.");
            if (args.Contains("--check"))
            {
                using var api = new PrintingApi(settings);
                api.Connect().GetAwaiter().GetResult();
                Store.Status("Connected to papey.ir; printer installed: " + settings.Printer); return;
            }
            using var mutex = new Mutex(true, "Local\\Papey.OrderPrinter", out var first);
            if (!first) return;
            ApplicationConfiguration.Initialize();
            Application.Run(new PrinterForm(settings, args.Contains("--test"), args.Contains("--preview")));
        }
        catch (Exception ex)
        {
            Store.Status("Startup failed: " + ex.GetType().Name);
            Environment.ExitCode = 1;
        }
    }
}
