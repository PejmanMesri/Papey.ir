using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Papey.Printer;

internal sealed record ThermalReceipt(byte[] Commands, byte[] PreviewPng, int Width, int Height)
{
    public static int WidthFor(string printer) => printer.Contains("SRP-332", StringComparison.OrdinalIgnoreCase) ? 576
        : printer.Contains("SRP-330", StringComparison.OrdinalIgnoreCase) ? 512
        : throw new InvalidOperationException("Select the BIXOLON SRP-330II/332II receipt printer.");

    public static ThermalReceipt Create(Bitmap source, int width)
    {
        if (width is not (512 or 576) || source.Width < 1 || source.Height < 1) throw new ArgumentException("Invalid receipt dimensions.");
        var height = checked((int)Math.Ceiling(source.Height * (double)width / source.Width));
        if (height > 40000) throw new InvalidOperationException("Receipt is too long.");
        using var scaled = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(scaled))
        {
            graphics.Clear(Color.White); graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(source, new Rectangle(0, 0, width, height));
        }
        var data = scaled.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var pixels = new byte[data.Stride * height];
        try { Marshal.Copy(data.Scan0, pixels, 0, pixels.Length); }
        finally { scaled.UnlockBits(data); }
        var rows = new byte[height * (width / 8)];
        var first = height; var last = -1;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var p = y * data.Stride + x * 4;
            if ((pixels[p + 2] * 299 + pixels[p + 1] * 587 + pixels[p] * 114) / 1000 >= 180) continue;
            rows[y * (width / 8) + x / 8] |= (byte)(0x80 >> (x % 8));
            first = Math.Min(first, y); last = Math.Max(last, y);
        }
        if (last < first) throw new InvalidOperationException("Receipt is blank; nothing was sent to the printer.");
        // Trim unused top/bottom page area. Retain only a small 1 mm breathing space.
        first = Math.Max(0, first - 7); last = Math.Min(height - 1, last + 7);
        var croppedHeight = last - first + 1;
        using var preview = scaled.Clone(new Rectangle(0, first, width, croppedHeight), PixelFormat.Format32bppArgb);
        using var png = new MemoryStream(); preview.Save(png, ImageFormat.Png);
        using var output = new MemoryStream();
        output.Write([0x1b, 0x40, 0x1b, 0x61, 0]); // Initialize; left alignment.
        const int chunkRows = 256;
        for (var row = first; row <= last; row += chunkRows)
        {
            var count = Math.Min(chunkRows, last - row + 1);
            var bytesWide = width / 8;
            output.Write([0x1d, 0x76, 0x30, 0, (byte)bytesWide, 0, (byte)(count & 255), (byte)(count >> 8)]);
            output.Write(rows, row * bytesWide, count * bytesWide);
        }
        output.Write([0x1d, 0x56, 66, 0]); // Feed only to the cutter position; partial cut.
        return new(output.ToArray(), png.ToArray(), width, croppedHeight);
    }
}
