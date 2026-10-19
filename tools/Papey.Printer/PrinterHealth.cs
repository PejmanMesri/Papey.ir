using System.Management;

namespace Papey.Printer;

internal static class PrinterHealth
{
    public static void EnsureReady(string printer)
    {
        var found = false;
        using var printers = new ManagementObjectSearcher("SELECT Name, WorkOffline FROM Win32_Printer");
        using var printerResults = printers.Get();
        foreach (ManagementObject item in printerResults)
        {
            using (item)
            {
                if (!string.Equals(item["Name"] as string, printer, StringComparison.OrdinalIgnoreCase)) continue;
                found = true;
                if (item["WorkOffline"] is true) throw new IOException("Printer is offline.");
            }
        }
        if (!found) throw new IOException("Printer is missing.");
        using var jobs = new ManagementObjectSearcher("SELECT Name, StatusMask FROM Win32_PrintJob");
        using var jobResults = jobs.Get();
        foreach (ManagementObject job in jobResults)
        {
            using (job)
            {
                if (!(job["Name"] as string ?? "").StartsWith(printer + ",", StringComparison.OrdinalIgnoreCase)) continue;
                var status = Convert.ToUInt32(job["StatusMask"] ?? 0u);
                // Paused, error, offline, out of paper, blocked, or user intervention.
                if ((status & (1u | 2u | 32u | 64u | 512u | 1024u)) != 0)
                    throw new IOException("Printer queue needs attention.");
            }
        }
    }
}
