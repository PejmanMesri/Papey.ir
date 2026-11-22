using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Papey.Printer;

internal static class PrinterHealth
{
    private const int InsufficientBuffer = 122;
    private const uint WorkOffline = 0x400;
    // Paused, error, paper jam/out/problem, offline, unavailable, intervention, or cover open.
    private const uint PrinterProblems = 0x005010DB;
    // Paused, error, offline, out of paper, blocked, or user intervention.
    private const uint JobProblems = 0x663;

    [StructLayout(LayoutKind.Sequential)]
    private struct PrinterInfo
    {
        public IntPtr Server, Printer, Share, Port, Driver, Comment, Location, DevMode,
            Separator, Processor, DataType, Parameters, Security;
        public uint Attributes, Priority, DefaultPriority, StartTime, UntilTime, Status, Jobs, AveragePpm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobInfo
    {
        public uint Id;
        public IntPtr Printer, Machine, User, Document, DataType, StatusText;
        public uint Status, Priority, Position, TotalPages, PagesPrinted;
        public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds;
    }

    [DllImport("winspool.drv", EntryPoint = "GetPrinterW", SetLastError = true)]
    private static extern bool GetPrinter(IntPtr printer, uint level, IntPtr buffer, uint size, out uint needed);

    [DllImport("winspool.drv", EntryPoint = "EnumJobsW", SetLastError = true)]
    private static extern bool EnumJobs(IntPtr printer, uint first, uint count, uint level, IntPtr buffer,
        uint size, out uint needed, out uint returned);

    public static Task EnsureReadyAsync(string printer) => Task.Run(() => EnsureReady(printer));

    private static void EnsureReady(string name)
    {
        if (!RawPrinter.OpenPrinter(name, out var printer, IntPtr.Zero))
            throw SpoolerError("open the selected printer", Marshal.GetLastWin32Error());
        try
        {
            var info = ReadPrinter(printer);
            if ((info.Attributes & WorkOffline) != 0 || (info.Status & PrinterProblems) != 0)
                throw new IOException($"Windows reports a printer problem (status 0x{info.Status:X}, attributes 0x{info.Attributes:X}). Check the printer, paper and USB connection.");
            if (info.Jobs > 0) CheckJobs(printer, info.Jobs);
        }
        finally { RawPrinter.ClosePrinter(printer); }
    }

    private static PrinterInfo ReadPrinter(IntPtr printer)
    {
        GetPrinter(printer, 2, IntPtr.Zero, 0, out var needed);
        var error = Marshal.GetLastWin32Error();
        if (error != InsufficientBuffer) throw SpoolerError("read printer status", error);

        var buffer = Marshal.AllocHGlobal(checked((int)needed));
        try
        {
            if (!GetPrinter(printer, 2, buffer, needed, out _))
                throw SpoolerError("read printer status", Marshal.GetLastWin32Error());
            return Marshal.PtrToStructure<PrinterInfo>(buffer);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static void CheckJobs(IntPtr printer, uint count)
    {
        if (EnumJobs(printer, 0, count, 1, IntPtr.Zero, 0, out var needed, out _)) return;
        var error = Marshal.GetLastWin32Error();
        if (error != InsufficientBuffer) throw SpoolerError("read the print queue", error);

        var buffer = Marshal.AllocHGlobal(checked((int)needed));
        try
        {
            if (!EnumJobs(printer, 0, count, 1, buffer, needed, out _, out var returned))
                throw SpoolerError("read the print queue", Marshal.GetLastWin32Error());
            var size = Marshal.SizeOf<JobInfo>();
            for (var index = 0; index < returned; index++)
            {
                var job = Marshal.PtrToStructure<JobInfo>(IntPtr.Add(buffer, checked(index * size)));
                if ((job.Status & JobProblems) != 0)
                    throw new IOException($"Windows print job {job.Id} needs attention (status 0x{job.Status:X}). Open the Windows print queue.");
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static Win32Exception SpoolerError(string operation, int error) =>
        new(error, $"Windows could not {operation} (error {error}). Check Print Spooler and the printer driver.");
}
