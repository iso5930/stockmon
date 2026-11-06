using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;
using StockMon.Core;
using StockMon.Infrastructure;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
try
{
    var verb = args.Length == 0 ? "fake-once" : args[0];
    if (args.Length > 1 || verb is not ("fake-once" or "migrate"))
    {
        Console.Error.WriteLine("Usage: StockMon.Worker [fake-once|migrate]");
        return 2;
    }
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
        Args = [], ContentRootPath = AppContext.BaseDirectory
    });
    using var host = builder.Build();
    if (verb == "migrate")
    {
        var file = Environment.GetEnvironmentVariable("STOCKMON_MIGRATION_DSN_FILE");
        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
        {
            Console.Error.WriteLine("configuration_error: STOCKMON_MIGRATION_DSN_FILE must name a readable secret file.");
            return 2;
        }
        var dsn = (await File.ReadAllTextAsync(file, cancellation.Token)).Trim();
        await using var source = NpgsqlDataSource.Create(dsn);
        await DatabaseMigrator.ApplyAsync(source, cancellation.Token);
        Console.WriteLine("migration_complete");
        return 0;
    }
    if (builder.Configuration["Monitor:Mode"] != "Fake")
    {
        Console.Error.WriteLine("verification_required: only Fake mode is implemented.");
        return 2;
    }
    if (!Enum.TryParse<FakeScenario>(builder.Configuration["Monitor:Scenario"], out var scenario) || !Enum.IsDefined(scenario))
    {
        Console.Error.WriteLine("configuration_error: unknown Fake scenario.");
        return 2;
    }
    var account = new Account("SYNTH-ACCOUNT", "SYNTH-OWNER", Broker.Kis, AccountType.Domestic, TradingEnvironment.Demo);
    IReadOnlyBroker broker = new FakeReadOnlyBroker(account, scenario, TimeProvider.System);
    var batch = await broker.FetchPositionsAsync(account.Id, cancellation.Token);
    Console.WriteLine(JsonSerializer.Serialize(new { mode = "fake", quality = batch.Quality.ToString(), batch.Pages, count = batch.Positions.Count, batch.AsOf }));
    return 0;
}
catch (OperationCanceledException) { Console.Error.WriteLine("cancelled"); return 130; }
catch (BrokerFailure e) { Console.Error.WriteLine($"synthetic_failure: {e.Code}"); return 1; }
catch (TimeoutException) { Console.Error.WriteLine("synthetic_timeout"); return 1; }
catch (Exception)
{
    // Never print provider exceptions or configuration values: they may contain credentials.
    Console.Error.WriteLine("operation_failed: check configuration and database availability; details suppressed.");
    return 1;
}
