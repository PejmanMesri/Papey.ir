using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Papey.Services;

public static class DataProtectionConfiguration
{
    public static IDataProtectionBuilder AddPapeyDataProtection(this IServiceCollection services,
        IConfiguration configuration, IHostEnvironment environment)
    {
        // Release directories change during deployments; the cookie identity must not.
        // Existing installations can retain their previous discriminator via configuration.
        var applicationName = configuration["DataProtection:ApplicationName"];
        if (string.IsNullOrWhiteSpace(applicationName)) applicationName = "Papey.Storefront";
        var keyPath = configuration["DataProtection:KeyPath"];
        if (string.IsNullOrWhiteSpace(keyPath))
            keyPath = Path.Combine(environment.ContentRootPath, "App_Data", "data-protection");

        return services.AddDataProtection()
            .SetApplicationName(applicationName)
            .PersistKeysToFileSystem(new DirectoryInfo(keyPath));
    }
}
