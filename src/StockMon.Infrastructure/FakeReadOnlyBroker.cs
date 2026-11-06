using StockMon.Core;

namespace StockMon.Infrastructure;

public enum FakeScenario { Normal, MultiPage, Unauthorized, RateLimited, Timeout, PartialPage }
public sealed class BrokerFailure(string code, int completedPages = 0, TimeSpan? retryAfter = null) : Exception(code)
{
    public string Code { get; } = code;
    public int CompletedPages { get; } = completedPages;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

public sealed class FakeReadOnlyBroker(Account account, FakeScenario scenario, TimeProvider timeProvider) : IReadOnlyBroker
{
    private void Check(string accountId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (accountId != account.Id) throw new UnauthorizedAccessException("Account scope denied.");
        if (!Enum.IsDefined(scenario)) throw new ArgumentException("Unknown fake scenario.");
    }

    private void FailIfRequested()
    {
        switch (scenario)
        {
            case FakeScenario.Unauthorized: throw new BrokerFailure("401");
            case FakeScenario.RateLimited: throw new BrokerFailure("429", retryAfter: TimeSpan.FromSeconds(3));
            case FakeScenario.Timeout: throw new TimeoutException("Synthetic timeout.");
        }
    }

    public Task<CapabilitySet> GetCapabilitiesAsync(string accountId, CancellationToken cancellationToken)
    {
        Check(accountId, cancellationToken);
        return Task.FromResult(new CapabilitySet(true, "synthetic_only", ["positions", "cash"]));
    }

    public async Task<PositionBatch> FetchPositionsAsync(string accountId, CancellationToken cancellationToken)
    {
        Check(accountId, cancellationToken); FailIfRequested();
        var asOf = timeProvider.GetUtcNow();
        var positions = new List<Position>
        {
            new(account, new("SYNTH-A", "SYNTHA", "XKRX", "KRW"), 10m, 100m, asOf, DataQuality.Synthetic)
        };
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        if (scenario == FakeScenario.PartialPage) throw new BrokerFailure("partial_page_timeout", 1);
        var pages = 1;
        if (scenario == FakeScenario.MultiPage)
        {
            positions.Add(new(account, new("SYNTH-B", "SYNTHB", "XKRX", "KRW"), 20m, 130m, asOf, DataQuality.Synthetic));
            pages++;
        }
        return new(accountId, positions.AsReadOnly(), pages, asOf, DataQuality.Synthetic);
    }

    public Task<CashBatch> FetchCashAsync(string accountId, CancellationToken cancellationToken)
    {
        Check(accountId, cancellationToken); FailIfRequested();
        return Task.FromResult(new CashBatch(accountId, [new("KRW", 1000m)], timeProvider.GetUtcNow(), DataQuality.Synthetic));
    }

    public Task<QuoteBatch> FetchQuotesAsync(IReadOnlyList<string> instruments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotSupportedException("unsupported: quotes are outside the initial fake fixture.");
    }

    public Task<BarBatch> FetchDailyBarsAsync(string instrument, DateOnly start, DateOnly end, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotSupportedException("unsupported: daily bars are outside the initial fake fixture.");
    }
}
