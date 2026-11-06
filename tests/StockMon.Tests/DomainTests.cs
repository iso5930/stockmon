using StockMon.Core;

namespace StockMon.Tests;

public sealed class DomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 0, 0, 0, TimeSpan.Zero);
    private static readonly Account Other = new("ACC-05", "OWNER-A", Broker.Kis, AccountType.Domestic, TradingEnvironment.Live);

    [Fact]
    public void SixLogicalAccountsHaveExpectedOwnersAndRejectDuplicatesAndSpoofedOwner()
    {
        var registry = new AccountRegistry([new("OWNER-SELF"), new("OWNER-A")]);
        for (var i = 1; i <= 6; i++)
        {
            var owner = i == 5 ? "OWNER-A" : "OWNER-SELF";
            var type = i switch { 1 => AccountType.Isa, 3 => AccountType.Overseas, 6 => AccountType.Pension, _ => AccountType.Domestic };
            registry.Add(new($"ACC-{i:00}", owner, i == 4 ? Broker.Shinhan : Broker.Kis, type, TradingEnvironment.Live));
        }
        Assert.Equal("OWNER-A", registry.Require("ACC-05", "OWNER-A").OwnerId);
        Assert.Throws<UnauthorizedAccessException>(() => registry.Require("ACC-05", "OWNER-SELF"));
        Assert.Throws<ArgumentException>(() => registry.Add(Other));
        Assert.Throws<ArgumentException>(() => registry.Add(new("ACC-07", "UNKNOWN", Broker.Kis, AccountType.Domestic, TradingEnvironment.Live)));
    }

    [Fact]
    public void DataContractsPreserveDecimalAndRequireUtcAndExplicitCurrency()
    {
        var instrument = new Instrument("SYNTH-A", "SYNTHA", "XKRX", "KRW");
        var position = new Position(Other, instrument, 0.1m + 0.2m, 124.285714285714m, Now, DataQuality.Synthetic);
        Assert.Equal(0.3m, position.Quantity);
        Assert.Equal(124.285714285714m, position.AverageCost);
        Assert.Throws<ArgumentException>(() => new Position(Other, instrument, 1m, 1m, Now.ToOffset(TimeSpan.FromHours(9)), DataQuality.Complete));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Position(Other, instrument, -1m, 1m, Now, DataQuality.Complete));
        Assert.Throws<ArgumentException>(() => new Instrument("A", "A", "XKRX", "UNKNOWN"));
        Assert.Throws<ArgumentException>(() => new Instrument("A", "A", "kr", "KRW"));
    }

    [Fact]
    public void CrossOwnerRequiresBothGrantAndConsentAndRevocationTakesEffect()
    {
        var principal = new Principal("VIEWER", PrincipalRole.AccountViewer, "OWNER-SELF");
        var grant = new AccessGrant(principal.Id, Other.Id, Permission.Detail, Now.AddDays(1), null, "fixture-grant");
        var consent = new Consent(Other.OwnerId, principal.Id, Permission.Detail, Now.AddDays(1), null, "fixture-consent");
        Assert.False(Authorization.Allows(principal, Other, Permission.Detail, [], [], Now));
        Assert.False(Authorization.Allows(principal, Other, Permission.Detail, [grant], [], Now));
        Assert.True(Authorization.Allows(principal, Other, Permission.Detail, [grant], [consent], Now));
        Assert.False(Authorization.Allows(principal, Other, Permission.Detail, [grant with { ExpiresAt = Now }], [consent], Now));
        Assert.False(Authorization.Allows(principal, Other, Permission.Detail, [grant], [consent with { RevokedAt = Now }], Now));
        Assert.False(Authorization.Allows(principal, Other, Permission.Aggregate, [grant], [consent], Now));
    }

    [Theory]
    [InlineData(PrincipalRole.Operator)]
    [InlineData(PrincipalRole.RiskViewer)]
    [InlineData(PrincipalRole.Collector)]
    [InlineData(PrincipalRole.Analyst)]
    [InlineData(PrincipalRole.Notifier)]
    public void OperationalRolesCannotReadDetailedAccountsEvenWithGrant(PrincipalRole role)
    {
        var p = new Principal("SERVICE", role, Other.OwnerId);
        var grant = new AccessGrant(p.Id, Other.Id, Permission.Detail, Now.AddDays(1), null, "fixture");
        Assert.False(Authorization.Allows(p, Other, Permission.Detail, [grant], [], Now));
    }

    [Fact]
    public void OwnerHasOnlyOwnDetailAndAggregateAndCollectorCannotCrossOwners()
    {
        var owner = new Principal("OWNER-LOGIN", PrincipalRole.Owner, Other.OwnerId);
        Assert.True(Authorization.Allows(owner, Other, Permission.Detail, [], [], Now));
        Assert.True(Authorization.Allows(owner, Other, Permission.Aggregate, [], [], Now));
        Assert.False(Authorization.Allows(owner, Other, Permission.Notify, [], [], Now));
        var collector = new Principal("COLLECTOR", PrincipalRole.Collector, "OWNER-SELF");
        var grant = new AccessGrant(collector.Id, Other.Id, Permission.Collect, Now.AddDays(1), null, "fixture");
        var consent = new Consent(Other.OwnerId, collector.Id, Permission.Collect, Now.AddDays(1), null, "fixture");
        Assert.False(Authorization.Allows(collector, Other, Permission.Collect, [grant], [consent], Now));
    }
}
