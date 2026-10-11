using Microsoft.Extensions.Configuration;
using Npgsql;
using Papey.Services;

static void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    Console.WriteLine($"PASS: {description}");
}

var clock = new TestClock();
var tickets = new OtpSignInTickets(clock);
Check(tickets.Consume(null) is null && tickets.Consume("1") is null, "Missing or user-ID-only proof is rejected");
var token = tickets.Create(42);
Check(tickets.Consume(new string('0', 64)) is null, "Guessed tickets are rejected");
Check(tickets.Consume(token) == 42 && tickets.Consume(token) is null, "Verified ticket is single-use");
var expired = tickets.Create(7);
clock.Now = clock.Now.AddMinutes(1);
Check(tickets.Consume(expired) is null, "Expired ticket is rejected at the expiry boundary");
var concurrent = tickets.Create(99);
var winners = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => tickets.Consume(concurrent))));
Check(winners.Count(value => value == 99) == 1, "Concurrent redemption signs in at most once");

var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["DATABASE_URL"] = "postgresql://papey:p%40ss%3Bword%3Awith%2Fsymbols@database.internal/papeydb"
}).Build();
var parsed = new NpgsqlConnectionStringBuilder(DatabaseConnection.Build(config));
Check(parsed.Host == "database.internal" && parsed.Port == 5432 && parsed.Database == "papeydb"
    && parsed.Username == "papey" && parsed.Password == "p@ss;word:with/symbols" && parsed.SslMode == SslMode.Require,
    "Managed database URL decodes credentials and requires TLS");
config["DATABASE_URL"] = null;
config["POSTGRES_PASSWORD"] = "secret;Host=attacker";
parsed = new NpgsqlConnectionStringBuilder(DatabaseConnection.Build(config));
Check(parsed.Host == "localhost" && parsed.Password == "secret;Host=attacker", "Database password cannot inject connection settings");
config["ConnectionStrings:DefaultConnection"] = "Host=custom;Database=store;Username=user;Password=secret";
Check(new NpgsqlConnectionStringBuilder(DatabaseConnection.Build(config)).Host == "custom", "Explicit connection configuration takes precedence");

sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => Now;
}
