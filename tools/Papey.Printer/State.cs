using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Papey.Printer;

internal static class Store
{
    public static readonly string Folder = Environment.GetEnvironmentVariable("PAPEY_PRINTER_DATA") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Papey", "Printer");
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public static string PathFor(string name) => Path.Combine(Folder, name);
    public static T? Read<T>(string name) => File.Exists(PathFor(name))
        ? JsonSerializer.Deserialize<T>(File.ReadAllText(PathFor(name)), Json) : default;
    public static void Write<T>(string name, T value)
    {
        Directory.CreateDirectory(Folder);
        var path = PathFor(name);
        using (var stream = new FileStream(path + ".new", FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        { JsonSerializer.Serialize(stream, value, Json); stream.Flush(true); }
        File.Move(path + ".new", path, true);
    }
    public static string Protect(string value) => Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
    public static string Unprotect(string value) => Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser));
    public static void Status(string message)
    {
        Write("status.json", new { at = DateTimeOffset.Now, message });
        // Do not log receipt contents, customer data, credentials, or lease tokens.
        var log = PathFor("agent.log");
        if (File.Exists(log) && new FileInfo(log).Length > 1_000_000) File.Move(log, log + ".old", true);
        File.AppendAllText(log, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
    }
}

internal sealed record Settings(string Server, string Printer, string Username, string ProtectedPassword, string ClientId, string PrintMode = "raw");
internal sealed record Lease(Guid JobId, int OrderId, string OrderNumber, string ReceiptUrl, Guid LeaseToken);
internal sealed record Journal(Lease Lease, string State);

internal enum Recovery { None, ReleasePreparation, Acknowledge, Review }
internal static class JournalPolicy
{
    public static Recovery Decide(Journal? journal) => journal?.State switch
    {
        null => Recovery.None,
        "Prepared" => Recovery.ReleasePreparation,
        "Submitted" => Recovery.Acknowledge,
        _ => Recovery.Review
    };
}
