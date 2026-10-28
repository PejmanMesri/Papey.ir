using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Papey.Services;

namespace Papey.Endpoints;

public static class CustomDesignEndpoints
{
    public static void MapCustomDesignEndpoints(this WebApplication app)
    {
        app.MapPost("/api/custom-designs", async (HttpContext http, IAntiforgery antiforgery, CustomDesignService designs) =>
        {
            try
            {
                await antiforgery.ValidateRequestAsync(http);
                if (!http.Request.HasFormContentType) return Results.BadRequest(new { error = "فرمت درخواست معتبر نیست." });
                var form = await http.Request.ReadFormAsync(http.RequestAborted);
                var request = await designs.SubmitAsync(http.User, form, http.RequestAborted);
                return Results.Ok(new { number = request.Number, url = "/account/custom-designs" });
            }
            catch (AntiforgeryValidationException) { return Results.BadRequest(new { error = "نشست منقضی شده است؛ صفحه را تازه کنید." }); }
            catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (InvalidDataException) { return Results.BadRequest(new { error = "حجم فایل‌ها بیش از حد مجاز است." }); }
        }).RequireAuthorization().RequireRateLimiting("custom-designs")
            .WithMetadata(new RequestSizeLimitAttribute(26 * 1024 * 1024), new RequestFormLimitsAttribute { MultipartBodyLengthLimit = 26 * 1024 * 1024 });

        app.MapGet("/api/custom-designs/{id:guid}/files/{file:int}", async (Guid id, int file, bool? download, HttpContext http, CustomDesignService designs) =>
        {
            http.Response.Headers.CacheControl = "private, no-store";
            http.Response.Headers.XContentTypeOptions = "nosniff";
            if (file is < 0 or > 7 || !await designs.MayReadAsync(http.User, id, http.RequestAborted)) return Results.NotFound();
            var path = Path.Combine(designs.Folder(id), $"{file}.png");
            return File.Exists(path) ? Results.File(path, "image/png", download == true ? $"papey-{id:N}-{file + 1}.png" : null) : Results.NotFound();
        }).RequireAuthorization();
    }
}
