namespace StockMon.Core;

public sealed record CapabilitySet(bool Synthetic, string State, IReadOnlyList<string> Functions);
public sealed record PositionBatch(string AccountId, IReadOnlyList<Position> Positions, int Pages,
    DateTimeOffset AsOf, DataQuality Quality);
public sealed record CashBalance(string Currency, decimal Amount);
public sealed record CashBatch(string AccountId, IReadOnlyList<CashBalance> Balances, DateTimeOffset AsOf, DataQuality Quality);
public sealed record QuoteBatch(string State);
public sealed record BarBatch(string State);

public interface IReadOnlyBroker
{
    Task<CapabilitySet> GetCapabilitiesAsync(string accountId, CancellationToken cancellationToken);
    Task<PositionBatch> FetchPositionsAsync(string accountId, CancellationToken cancellationToken);
    Task<CashBatch> FetchCashAsync(string accountId, CancellationToken cancellationToken);
    Task<QuoteBatch> FetchQuotesAsync(IReadOnlyList<string> instruments, CancellationToken cancellationToken);
    Task<BarBatch> FetchDailyBarsAsync(string instrument, DateOnly start, DateOnly end, CancellationToken cancellationToken);
}
