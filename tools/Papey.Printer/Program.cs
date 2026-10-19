using System.Drawing.Printing;

namespace Papey.Printer;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            Directory.CreateDirectory(Store.Folder);
            if (args.Contains("--install"))
            {
                var target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "PapeyPrinter");
                Directory.CreateDirectory(target);
                if (!Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var source in Directory.EnumerateFiles(AppContext.BaseDirectory, "*", SearchOption.AllDirectories))
                    {
                        var destination = Path.Combine(target, Path.GetRelativePath(AppContext.BaseDirectory, source));
                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(source, destination, true);
                    }
                }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(target, "Papey.Printer.exe"), "--setup") { UseShellExecute = true });
                return;
            }
            if (args.Contains("--diagnose"))
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "papey-printer-diagnostic.txt"),
                    $"Data={Store.Folder}; Settings={File.Exists(Store.PathFor("settings.json"))}; Printers={PrinterSettings.InstalledPrinters.Count}");
                return;
            }
            if (args.Length == 3 && args[0] == "--configure")
            {
                // Provision from an existing local credential file, never a command-line password.
                var old = Store.Read<Settings>("settings.json");
                Store.Write("settings.json", new Settings("https://papey.ir/", args[2], "Admin",
                    Store.Protect(File.ReadAllText(args[1]).Trim()), old?.ClientId ?? "papey-" + Guid.NewGuid().ToString("N")));
                Store.Status("Configured for papey.ir and " + args[2]); return;
            }
            var settings = Store.Read<Settings>("settings.json");
            ApplicationConfiguration.Initialize();
            if (args.Contains("--setup") || settings is null)
            {
                using var setup = new SetupForm();
                if (setup.ShowDialog() != DialogResult.OK) return;
                settings = setup.Result!;
            }
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
            Application.Run(new PrinterForm(settings, args.Contains("--test"), args.Contains("--preview")));
        }
        catch (Exception ex)
        {
            try { Store.Status("Startup failed: " + ex.GetType().Name); }
            catch { /* A profile/storage error must not expose credentials in a dialog. */ }
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "papey-printer-startup-error.txt"), ex.GetType().Name);
            Environment.ExitCode = 1;
        }
    }
}
