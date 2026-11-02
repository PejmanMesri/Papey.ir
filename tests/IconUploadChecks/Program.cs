using System.Text;
using Papey.Services;

var checks = 0;
void Check(bool value) { if (!value) throw new Exception("Icon validation check failed"); checks++; }
void Reject(string svg) {
    try { ProductTypeIconUploads.Validate(Encoding.UTF8.GetBytes(svg), ".svg"); }
    catch (InvalidOperationException) { checks++; return; }
    throw new Exception("Unsafe SVG accepted");
}
var clean = "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 64 64'><path d='M0 0L20 20' fill='none' stroke='#333'/></svg>";
Check(ProductTypeIconUploads.Validate(Encoding.UTF8.GetBytes(clean), ".svg").Length > 0);
Reject("<svg xmlns='http://www.w3.org/2000/svg' onload='alert(1)'/>");
Reject("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>");
Reject("<svg xmlns='http://www.w3.org/2000/svg'><image href='https://example.com/a'/></svg>");
Reject("<svg xmlns='http://www.w3.org/2000/svg'><path fill='url(https://example.com/a)'/></svg>");
Reject("<!DOCTYPE svg [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><svg xmlns='http://www.w3.org/2000/svg'>&x;</svg>");
Reject("<svg xmlns='http://www.w3.org/2000/svg'><foreignObject/></svg>");
try { ProductTypeIconUploads.Validate("<html>bad</html>"u8.ToArray(), ".png"); throw new Exception("Bad PNG accepted"); } catch (InvalidOperationException) { checks++; }
try { ProductTypeIconUploads.Validate(new byte[2 * 1024 * 1024 + 1], ".png"); throw new Exception("Oversize accepted"); } catch (InvalidOperationException) { checks++; }
foreach (var extension in new[] { "png", "jpg", "jpeg", "webp", "gif", "avif", "ico", "bmp", "svg" })
{
    var path = "/uploads/type-icons/" + new string('a', 32) + "." + extension;
    Check(path.Length <= 64 && ProductTypeIconUploads.IsUploaded(path));
    Check(ProductTypeIcons.Image("hoodie", path) == path);
}
Check(!ProductTypeIconUploads.IsUploaded("/uploads/type-icons/../../evil.svg"));
Check(!ProductTypeIconUploads.IsUploaded("https://evil.test/icon.svg"));
Check(ProductTypeIcons.Image("hoodie", "tshirt") == "/img/icons/tshirt.svg");
foreach (var path in Directory.GetFiles("wwwroot/img/icons", "*.svg"))
    Check(ProductTypeIconUploads.Validate(File.ReadAllBytes(path), ".svg").Length > 0);
Console.WriteLine($"PASS: {checks} icon upload validation and selection checks");
