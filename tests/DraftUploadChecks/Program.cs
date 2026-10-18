using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Papey.Services;

var root = Path.Combine(Path.GetTempPath(), "papey-draft-checks-" + Guid.NewGuid().ToString("N"));
var env = new TestEnvironment { ContentRootPath = root, WebRootPath = Path.Combine(root, "wwwroot") };
var clock = new TestClock();
var uploads = new ProductDraftUploads(env, clock);
int count = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); count++; Console.WriteLine("PASS: " + message); }
try
{
    using var input = new MemoryStream([1, 2, 3, 4]);
    var photo = await uploads.StageAsync(input, "../shirt.jpg");
    Check(!photo.Name.Contains('/') && photo.PreviewUrl.StartsWith("/admin/product-drafts/"), "Draft gets an opaque name and private preview route");
    using (var preview = uploads.OpenPreview(photo.Name)) Check(preview?.Length == 4, "Preview reads the staged photo");
    Check(!Directory.Exists(Path.Combine(env.WebRootPath, "uploads")), "Staging does not publish permanent product media");
    try { uploads.OpenPreview("../secret.jpg"); throw new Exception("Traversal was allowed"); }
    catch (ArgumentException) { Check(true, "Preview rejects path traversal"); }
    try { await uploads.StageAsync(new MemoryStream([0]), "script.html"); throw new Exception("HTML was allowed"); }
    catch (InvalidOperationException) { Check(true, "Unsupported active content cannot be staged"); }
    var permanent = uploads.CopyToPermanent(photo, false);
    Check(File.Exists(permanent) && uploads.OpenPreview(photo.Name) is { } retained && Close(retained), "Promotion keeps the draft available until commit");
    File.Delete(permanent); // Simulate rollback after a failed database save.
    permanent = uploads.CopyToPermanent(photo, false);
    uploads.Delete(photo);
    Check(File.Exists(permanent) && uploads.OpenPreview(photo.Name) is null, "Commit cleanup removes only the draft and preserves the saved photo");
    var design = await uploads.StageAsync(new MemoryStream([9]), "print.png");
    var privatePath = uploads.CopyToPermanent(design, true);
    Check(privatePath.StartsWith(Path.Combine(root, "App_Data", "product-designs")), "Designs stay outside the public web root");
    var abandoned = await uploads.StageAsync(new MemoryStream([8]), "abandoned.jpg");
    clock.Now = clock.Now.AddHours(23);
    Check(uploads.Cleanup() == 0, "Cleanup retains uploads younger than 24 hours");
    var fresh = await uploads.StageAsync(new MemoryStream([7]), "fresh.jpg");
    clock.Now = clock.Now.AddHours(2);
    Check(uploads.Cleanup() == 2 && uploads.OpenPreview(abandoned.Name) is null, "Cleanup expires abandoned uploads older than 24 hours");
    using (var preview = uploads.OpenPreview(fresh.Name)) Check(preview is not null && File.Exists(permanent) && File.Exists(privatePath), "Cleanup preserves fresh drafts and all permanent files");
    try { uploads.CopyToPermanent(abandoned, false); throw new Exception("Expired draft was promoted"); }
    catch (IOException) { Check(true, "Expired drafts fail explicitly instead of creating broken image links"); }
    Console.WriteLine($"{count} draft upload checks passed.");
}
finally { Directory.Delete(root, true); }
static bool Close(Stream stream) { stream.Dispose(); return true; }

sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => Now;
}
sealed class TestEnvironment : IWebHostEnvironment
{
    public string EnvironmentName { get; set; } = "Development";
    public string ApplicationName { get; set; } = "DraftUploadChecks";
    public string WebRootPath { get; set; } = "";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string ContentRootPath { get; set; } = "";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
