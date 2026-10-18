namespace Papey.Services;

/// <summary>Private staging area for files selected before a product is created.</summary>
public sealed class ProductDraftUploads(IWebHostEnvironment env, TimeProvider clock)
{
    private readonly object _gate = new();
    private string Folder => Path.Combine(env.ContentRootPath, "App_Data", "product-drafts");
    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png",
        [".webp"] = "image/webp", [".gif"] = "image/gif", [".avif"] = "image/avif",
        [".mp4"] = "video/mp4", [".webm"] = "video/webm"
    };

    public sealed record Upload(string Name, string ContentType)
    {
        public string PreviewUrl => "/admin/product-drafts/" + Name;
        public bool IsVideo => ContentType.StartsWith("video/");
    }

    private string GetPath(string name)
    {
        var extension = Path.GetExtension(name);
        if (Path.GetFileName(name) != name || !Types.ContainsKey(extension) ||
            !Guid.TryParseExact(Path.GetFileNameWithoutExtension(name), "N", out _))
            throw new ArgumentException("Invalid draft file name.");
        return Path.Combine(Folder, name);
    }

    public async Task<Upload> StageAsync(Stream input, string originalName)
    {
        var extension = Path.GetExtension(originalName).ToLowerInvariant();
        if (!Types.TryGetValue(extension, out var contentType))
            throw new InvalidOperationException("فرمت فایل مجاز نیست.");
        Directory.CreateDirectory(Folder);
        var name = Guid.NewGuid().ToString("N") + extension;
        var path = GetPath(name);
        var partial = path + ".partial";
        try
        {
            await using (var output = File.Create(partial)) await input.CopyToAsync(output);
            lock (_gate)
            {
                File.Move(partial, path);
                File.SetLastWriteTimeUtc(path, clock.GetUtcNow().UtcDateTime);
            }
            return new(name, contentType);
        }
        catch { File.Delete(partial); throw; }
    }

    public Stream? OpenPreview(string name)
    {
        lock (_gate)
        {
            var path = GetPath(name);
            return File.Exists(path) ? File.OpenRead(path) : null;
        }
    }

    public string ContentType(string name) => Types[Path.GetExtension(name)];

    // Copy first; the original is removed only after the database transaction commits.
    public string CopyToPermanent(Upload upload, bool design)
    {
        lock (_gate)
        {
            var source = GetPath(upload.Name);
            if (!File.Exists(source)) throw new IOException("فایل موقت منقضی شده است؛ دوباره آپلود کنید.");
            var folder = design ? Path.Combine(env.ContentRootPath, "App_Data", "product-designs")
                : Path.Combine(env.WebRootPath, "uploads", upload.IsVideo ? "videos" : "images");
            Directory.CreateDirectory(folder);
            var target = Path.Combine(folder, Guid.NewGuid().ToString("N") + Path.GetExtension(upload.Name));
            File.Copy(source, target);
            return target;
        }
    }

    public void Delete(Upload upload)
    {
        lock (_gate) File.Delete(GetPath(upload.Name));
    }

    public int Cleanup()
    {
        lock (_gate)
        {
            if (!Directory.Exists(Folder)) return 0;
            var cutoff = clock.GetUtcNow().UtcDateTime.AddHours(-24);
            var count = 0;
            foreach (var path in Directory.EnumerateFiles(Folder))
            {
                if (File.GetLastWriteTimeUtc(path) > cutoff) continue;
                try { File.Delete(path); count++; }
                catch (IOException) { /* An upload or preview may still hold the file open. */ }
            }
            return count;
        }
    }
}

public sealed class ProductDraftCleanupWorker(ProductDraftUploads uploads,
    ILogger<ProductDraftCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        do
        {
            try { logger.LogInformation("Removed {Count} expired product draft uploads", uploads.Cleanup()); }
            catch (Exception ex) { logger.LogError(ex, "Product draft upload cleanup failed"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
