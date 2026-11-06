using System.Text.Json;
using StockMon.Core;
using StockMon.Infrastructure;

namespace StockMon.Tests;

public sealed class BrokerTests
{
    private static readonly Account Account = new("SYNTH-ACCOUNT", "SYNTH-OWNER", Broker.Kis, AccountType.Domestic, TradingEnvironment.Demo);
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 6, 0, 0, 0, TimeSpan.Zero);
    }

    [Fact]
    public async Task RecordedScenariosReproduceCompleteAndFailedResponses()
    {
        using var fixtures = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "scenarios.json")));
        foreach (var row in fixtures.RootElement.EnumerateArray())
        {
            var scenario = Enum.Parse<FakeScenario>(row.GetProperty("scenario").GetString()!);
            var broker = new FakeReadOnlyBroker(Account, scenario, new FixedClock());
            var error = row.GetProperty("error").GetString();
            if (error == "timeout")
                await Assert.ThrowsAsync<TimeoutException>(() => broker.FetchPositionsAsync(Account.Id, default));
            else if (error is not null)
            {
                var failure = await Assert.ThrowsAsync<BrokerFailure>(() => broker.FetchPositionsAsync(Account.Id, default));
                Assert.Equal(error, failure.Code);
                Assert.Equal(row.GetProperty("pages").GetInt32(), failure.CompletedPages);
                if (error == "429") Assert.Equal(TimeSpan.FromSeconds(3), failure.RetryAfter);
            }
            else
            {
                var batch = await broker.FetchPositionsAsync(Account.Id, default);
                Assert.Equal(row.GetProperty("pages").GetInt32(), batch.Pages);
                Assert.Equal(row.GetProperty("positions").GetInt32(), batch.Positions.Count);
                Assert.Equal(DataQuality.Synthetic, batch.Quality);
                Assert.Equal(new FixedClock().GetUtcNow(), batch.AsOf);
                Assert.All(batch.Positions, p => Assert.Equal(Account.OwnerId, p.OwnerId));
                Assert.Equal(100m, batch.Positions[0].AverageCost);
            }
        }
    }

    [Fact]
    public async Task CancellationAndAccountScopeFailBeforeReturningData()
    {
        var broker = new FakeReadOnlyBroker(Account, FakeScenario.Normal, new FixedClock());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => broker.FetchPositionsAsync(Account.Id, new CancellationToken(true)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => broker.FetchPositionsAsync("ANOTHER", default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => broker.FetchCashAsync("ANOTHER", default));
        Assert.Equal(1000m, (await broker.FetchCashAsync(Account.Id, default)).Balances.Single().Amount);
        Assert.True((await broker.GetCapabilitiesAsync(Account.Id, default)).Synthetic);
        await Assert.ThrowsAsync<NotSupportedException>(() => broker.FetchQuotesAsync(["SYNTH-A"], default));
        await Assert.ThrowsAsync<NotSupportedException>(() => broker.FetchDailyBarsAsync("SYNTH-A", default, default, default));
    }

    [Fact]
    public void BrokerContractContainsOnlyTheApprovedReadOperations()
    {
        Assert.Equal(new[] { "FetchCashAsync", "FetchDailyBarsAsync", "FetchPositionsAsync", "FetchQuotesAsync", "GetCapabilitiesAsync" },
            typeof(IReadOnlyBroker).GetMethods().Select(m => m.Name).Order().ToArray());
        Assert.DoesNotContain(typeof(FakeReadOnlyBroker).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
            f => f.FieldType == typeof(HttpClient));
    }
}
