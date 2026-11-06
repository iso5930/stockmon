namespace StockMon.Core;

public enum Broker { Kis, Shinhan }
public enum AccountType { Isa, Domestic, Overseas, Pension }
public enum TradingEnvironment { Live, Demo }
public enum DataQuality { Synthetic, Complete, Partial, Stale, Unknown }
public enum Permission { Detail, Aggregate, Collect, Notify, Analyze }
public enum PrincipalRole { Owner, AccountViewer, RiskViewer, Operator, Collector, Analyst, Notifier }

public static class Contract
{
    public static string Id(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 80 ||
            value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new ArgumentException("Invalid logical identifier.");
        return value;
    }

    public static string Currency(string value) => value is "KRW" or "USD"
        ? value : throw new ArgumentException("Unsupported currency.");

    public static DateTimeOffset Utc(DateTimeOffset value) => value.Offset == TimeSpan.Zero
        ? value : throw new ArgumentException("Timestamp must use UTC offset zero.");
}

public sealed record Owner
{
    public string Id { get; }
    public Owner(string id) => Id = Contract.Id(id);
}

public sealed record Account
{
    public string Id { get; }
    public string OwnerId { get; }
    public Broker Broker { get; }
    public AccountType Type { get; }
    public TradingEnvironment Environment { get; }
    public Account(string id, string ownerId, Broker broker, AccountType type, TradingEnvironment environment)
    {
        Id = Contract.Id(id); OwnerId = Contract.Id(ownerId);
        if (!Enum.IsDefined(broker) || !Enum.IsDefined(type) || !Enum.IsDefined(environment))
            throw new ArgumentException("Unknown account classification.");
        Broker = broker; Type = type; Environment = environment;
    }
}

public sealed class AccountRegistry
{
    private readonly HashSet<string> owners;
    private readonly Dictionary<string, Account> accounts = new(StringComparer.Ordinal);
    public AccountRegistry(IEnumerable<Owner> owners) => this.owners = owners.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
    public void Add(Account account)
    {
        if (!owners.Contains(account.OwnerId)) throw new ArgumentException("Unknown owner.");
        if (!accounts.TryAdd(account.Id, account)) throw new ArgumentException("Duplicate logical account.");
    }
    public Account Require(string id, string ownerId) => accounts.TryGetValue(id, out var a) && a.OwnerId == ownerId
        ? a : throw new UnauthorizedAccessException("Account scope denied.");
}

public sealed record Instrument
{
    public string Id { get; }
    public string Code { get; }
    public string ExchangeMic { get; }
    public string Currency { get; }
    public Instrument(string id, string code, string exchangeMic, string currency)
    {
        Id = Contract.Id(id); Code = Contract.Id(code);
        if (exchangeMic.Length != 4 || exchangeMic.Any(c => !char.IsAsciiLetterUpper(c)))
            throw new ArgumentException("A four-letter MIC is required.");
        ExchangeMic = exchangeMic; Currency = Contract.Currency(currency);
    }
}

public sealed record Position
{
    public string AccountId { get; }
    public string OwnerId { get; }
    public Instrument Instrument { get; }
    public decimal Quantity { get; }
    public decimal AverageCost { get; }
    public DateTimeOffset AsOf { get; }
    public DataQuality Quality { get; }
    public Position(Account account, Instrument instrument, decimal quantity, decimal averageCost,
        DateTimeOffset asOf, DataQuality quality)
    {
        if (quantity < 0 || averageCost < 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (!Enum.IsDefined(quality)) throw new ArgumentException("Unknown quality.");
        AccountId = account.Id; OwnerId = account.OwnerId; Instrument = instrument;
        Quantity = quantity; AverageCost = averageCost; AsOf = Contract.Utc(asOf); Quality = quality;
    }
}

public sealed record Principal(string Id, PrincipalRole Role, string? OwnerId);
public sealed record AccessGrant(string PrincipalId, string AccountId, Permission Permission,
    DateTimeOffset ExpiresAt, DateTimeOffset? RevokedAt, string EvidenceRef);
public sealed record Consent(string OwnerId, string PrincipalId, Permission Permission,
    DateTimeOffset ExpiresAt, DateTimeOffset? RevokedAt, string EvidenceRef);

public static class Authorization
{
    // Principal is supplied by the trusted authentication boundary, never request owner_id.
    public static bool Allows(Principal principal, Account account, Permission permission,
        IEnumerable<AccessGrant> grants, IEnumerable<Consent> consents, DateTimeOffset now)
    {
        Contract.Utc(now);
        bool roleAllows = principal.Role switch
        {
            PrincipalRole.Owner => permission is Permission.Detail or Permission.Aggregate,
            PrincipalRole.AccountViewer => permission is Permission.Detail or Permission.Aggregate,
            PrincipalRole.RiskViewer => permission == Permission.Aggregate,
            PrincipalRole.Collector => permission == Permission.Collect && principal.OwnerId == account.OwnerId,
            PrincipalRole.Analyst => permission == Permission.Analyze,
            PrincipalRole.Notifier => permission == Permission.Notify,
            _ => false
        };
        if (!roleAllows) return false;
        if (principal.Role == PrincipalRole.Owner && principal.OwnerId == account.OwnerId) return true;
        bool granted = grants.Any(g => g.PrincipalId == principal.Id && g.AccountId == account.Id &&
            g.Permission == permission && g.ExpiresAt > now && g.RevokedAt is null && !string.IsNullOrWhiteSpace(g.EvidenceRef));
        bool consented = consents.Any(c => c.OwnerId == account.OwnerId && c.PrincipalId == principal.Id &&
            c.Permission == permission && c.ExpiresAt > now && c.RevokedAt is null && !string.IsNullOrWhiteSpace(c.EvidenceRef));
        return granted && (principal.OwnerId == account.OwnerId || consented);
    }
}
