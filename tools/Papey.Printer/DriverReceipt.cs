using System.Drawing.Printing;

namespace Papey.Printer;

/// <summary>Compatibility path for queues/drivers that do not accept RAW ESC/POS images.</summary>
internal static class DriverReceipt
{
    private const float PrintableWidth = 72f / 25.4f * 100f;
    public static PaperSize PaperFor(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentException("Invalid receipt dimensions.");
        var heightMm = height * 72d / width + 4;
        return new PaperSize("Papey 80mm receipt", (int)Math.Ceiling(80d / 25.4 * 100), (int)Math.Ceiling(heightMm / 25.4 * 100));
    }
    public static RectangleF Destination(RectangleF printableArea, int sourceWidth, int remainingRows)
    {
        if (printableArea.Width <= 0 || printableArea.Height <= 0 || sourceWidth <= 0 || remainingRows <= 0)
            throw new InvalidOperationException("Printer reported an invalid printable area.");
        var width = Math.Min(PrintableWidth, printableArea.Width);
        var height = Math.Min(remainingRows * width / sourceWidth, printableArea.Height);
        return new(printableArea.Left + (printableArea.Width - width) / 2, printableArea.Top, width, height);
    }
    public static int RowsFor(RectangleF printableArea, int sourceWidth, int remainingRows)
    {
        var target = Destination(printableArea, sourceWidth, remainingRows);
        return Math.Max(1, Math.Min(remainingRows, (int)Math.Floor(printableArea.Height * sourceWidth / target.Width)));
    }
    public static void Submit(string printer, ThermalReceipt receipt)
    {
        using var stream = new MemoryStream(receipt.PreviewPng);
        using var image = new Bitmap(stream);
        using var document = new PrintDocument { DocumentName = "Papey receipt", PrintController = new StandardPrintController(), OriginAtMargins = false };
        document.PrinterSettings.PrinterName = printer;
        if (!document.PrinterSettings.IsValid) throw new InvalidOperationException("Configured printer is not installed.");
        document.PrinterSettings.Copies = 1;
        document.PrinterSettings.PrintToFile = false;
        document.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
        document.DefaultPageSettings.PaperSize = PaperFor(image.Width, image.Height);
        var offset = 0;
        document.PrintPage += (_, e) =>
        {
            if (e.Graphics is null) throw new InvalidOperationException("Printer graphics are unavailable.");
            var target = Destination(e.PageSettings.PrintableArea, image.Width, image.Height - offset);
            // Preserve text size if a driver caps custom roll length: continue on
            // another page instead of shrinking or dropping the rest of the order.
            var rows = RowsFor(e.PageSettings.PrintableArea, image.Width, image.Height - offset);
            target.Height = rows * target.Width / image.Width;
            e.Graphics.PageUnit = GraphicsUnit.Display;
            e.Graphics.TranslateTransform(-e.PageSettings.HardMarginX, -e.PageSettings.HardMarginY);
            e.Graphics.DrawImage(image, target, new RectangleF(0, offset, image.Width, rows), GraphicsUnit.Pixel);
            offset += rows;
            e.HasMorePages = offset < image.Height;
        };
        document.Print();
    }
}
