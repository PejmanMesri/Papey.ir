using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Papey.Printer;

internal static class RawPrinter
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Document { public string Name; public string? Output; public string DataType; }
    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool OpenPrinter(string name, out IntPtr printer, IntPtr defaults);
    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint StartDocPrinter(IntPtr printer, uint level, ref Document document);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool WritePrinter(IntPtr printer, IntPtr buffer, uint length, out uint written);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool EndDocPrinter(IntPtr printer);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool AbortPrinter(IntPtr printer);
    [DllImport("winspool.drv")] private static extern bool ClosePrinter(IntPtr printer);

    public static uint Submit(string printerName, byte[] commands)
    {
        if (!OpenPrinter(printerName, out var printer, IntPtr.Zero)) throw new Win32Exception();
        var started = false;
        try
        {
            var document = new Document { Name = "Papey receipt", DataType = "RAW" };
            var jobId = StartDocPrinter(printer, 1, ref document);
            if (jobId == 0) throw new Win32Exception();
            started = true;
            var buffer = Marshal.AllocHGlobal(commands.Length);
            try
            {
                Marshal.Copy(commands, 0, buffer, commands.Length);
                var offset = 0;
                while (offset < commands.Length)
                {
                    if (!WritePrinter(printer, IntPtr.Add(buffer, offset), (uint)(commands.Length - offset), out var written) || written == 0)
                        throw new Win32Exception();
                    offset += checked((int)written);
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }
            if (!EndDocPrinter(printer)) throw new Win32Exception();
            started = false; return jobId;
        }
        finally { if (started) AbortPrinter(printer); ClosePrinter(printer); }
    }
}
