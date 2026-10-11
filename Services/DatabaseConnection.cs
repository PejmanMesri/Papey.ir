using Npgsql;
using Microsoft.Extensions.Configuration;

namespace Papey.Services;

public static class DatabaseConnection
{
    public static string Build(IConfiguration config)
    {
        if (!string.IsNullOrWhiteSpace(config.GetConnectionString("DefaultConnection")))
            return config.GetConnectionString("DefaultConnection")!;

        var url = config["DATABASE_URL"];
        if (!string.IsNullOrWhiteSpace(url))
        {
            var uri = new Uri(url);
            if (uri.Scheme is not ("postgres" or "postgresql"))
                throw new InvalidOperationException("DATABASE_URL must be a PostgreSQL URL.");
            var credentials = uri.UserInfo.Split(':', 2);
            return new NpgsqlConnectionStringBuilder
            {
                Host = uri.Host,
                Port = uri.Port > 0 ? uri.Port : 5432,
                Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
                Username = Uri.UnescapeDataString(credentials[0]),
                Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : "",
                SslMode = SslMode.Require
            }.ConnectionString;
        }

        return new NpgsqlConnectionStringBuilder
        {
            Host = config["DB_HOST"] ?? "localhost",
            Port = config.GetValue("DB_PORT", 5432),
            Database = config["POSTGRES_DB"] ?? "papeydb",
            Username = config["POSTGRES_USER"] ?? "papey",
            Password = config["POSTGRES_PASSWORD"] ?? "papey123"
        }.ConnectionString;
    }
}
