using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.UnitTests.Modules.TenancyLicensing;

public sealed class LicenseTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid Actor = Guid.Parse("018e0100-0000-7000-8000-000000000001");

    [Fact]
    public void CreateTenantWithLicenseRecordsInitialValuesAndVersion7Identifiers()
    {
        var tenant = CreateTenant();
        var license = tenant.License;
        var change = Assert.Single(license.Changes);

        Assert.Equal(7, tenant.Id.Version);
        Assert.Equal(7, license.Id.Version);
        Assert.Equal(7, change.Id.Version);
        Assert.Equal(tenant.Id, license.TenantId);
        Assert.Equal(tenant.Id, change.TenantId);
        Assert.Equal(license.Id, change.LicenseId);
        Assert.Equal("Botica", tenant.TradingName);
        Assert.Equal(LicenseChangeKind.Created, change.Kind);
        Assert.Equal(Actor, change.ActorId);
        Assert.Equal(Start, change.OccurredAt);
        Assert.Null(change.PreviousStatus);
        Assert.Null(change.PreviousStartsAt);
        Assert.Null(change.PreviousExpiresAt);
        Assert.Null(change.PreviousMaxBranches);
        Assert.Equal(LicenseStatus.Active, change.NewStatus);
        Assert.Equal(Start, change.NewStartsAt);
        Assert.Equal(Start.AddMonths(1), change.NewExpiresAt);
        Assert.Equal(3, change.NewMaxBranches);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public void TenantRejectsMissingTradingName(string? tradingName) =>
        Assert.ThrowsAny<ArgumentException>(() => Tenant.Create(tradingName!, Start, Start.AddMonths(1), 1,
            LicenseStatus.Active, Actor, Start));

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(int.MaxValue)]
    public void RejectsBranchLimitOutsideStandardProduct(int maxBranches) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => License.Create(Guid.NewGuid(), Start, Start.AddMonths(1),
            maxBranches, LicenseStatus.Active, Actor, Start));

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void AcceptsBoundaryBranchLimits(int maxBranches)
    {
        var license = License.Create(Guid.NewGuid(), Start, Start.AddMonths(1), maxBranches, LicenseStatus.Active, Actor, Start);
        Assert.True(license.AllowsOperation(Start));
        Assert.Equal(maxBranches, Assert.Single(license.Changes).NewMaxBranches);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void RejectsExpirationAtOrBeforeStart(int expirationOffsetDays) =>
        Assert.Throws<ArgumentException>(() => License.Create(Guid.NewGuid(), Start, Start.AddDays(expirationOffsetDays),
            1, LicenseStatus.Active, Actor, Start));

    [Fact]
    public void RejectsUndefinedStatusAndMissingIdentifiers()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateTenant((LicenseStatus)100));
        Assert.Throws<ArgumentException>(() => License.Create(Guid.Empty, Start, Start.AddMonths(1), 1, LicenseStatus.Active, Actor, Start));
        Assert.Throws<ArgumentException>(() => Tenant.Create("Botica", Start, Start.AddMonths(1), 1, LicenseStatus.Active, Guid.Empty, Start));
    }

    [Theory]
    [InlineData(LicenseStatus.Trial, true)]
    [InlineData(LicenseStatus.Active, true)]
    [InlineData(LicenseStatus.Grace, true)]
    [InlineData(LicenseStatus.Suspended, false)]
    [InlineData(LicenseStatus.Cancelled, false)]
    [InlineData(LicenseStatus.PurgePending, false)]
    [InlineData(LicenseStatus.Purged, false)]
    public void OperationGateEnforcesAllLicenseStates(LicenseStatus status, bool expected) =>
        Assert.Equal(expected, CreateTenant(status).License.AllowsOperation(Start.AddDays(1)));

    [Theory]
    [InlineData(LicenseStatus.Trial)]
    [InlineData(LicenseStatus.Active)]
    [InlineData(LicenseStatus.Grace)]
    public void OperationGateRequiresValidPeriodIncludingStartAndExcludingExpiration(LicenseStatus status)
    {
        var license = CreateTenant(status).License;

        Assert.False(license.AllowsOperation(Start.AddTicks(-1)));
        Assert.True(license.AllowsOperation(Start));
        Assert.True(license.AllowsOperation(license.ExpiresAt.AddTicks(-1)));
        Assert.False(license.AllowsOperation(license.ExpiresAt));
        Assert.False(license.AllowsOperation(license.ExpiresAt.AddDays(1)));
    }

    [Fact]
    public void AdministrativeDatesAreNormalizedToUtc()
    {
        var localStart = Start.ToOffset(TimeSpan.FromHours(-5));
        var tenant = Tenant.Create("Botica", localStart, localStart.AddMonths(1), 1, LicenseStatus.Trial, Actor, localStart);

        Assert.Equal(TimeSpan.Zero, tenant.CreatedAt.Offset);
        Assert.Equal(Start, tenant.CreatedAt);
        Assert.Equal(TimeSpan.Zero, tenant.License.StartsAt.Offset);
        Assert.Equal(TimeSpan.Zero, tenant.License.ExpiresAt.Offset);
        Assert.Equal(TimeSpan.Zero, Assert.Single(tenant.License.Changes).OccurredAt.Offset);
    }

    [Fact]
    public void RenewalExtendsExpirationWithActorAndBeforeAfterHistory()
    {
        var license = CreateTenant().License;
        var previousExpiration = license.ExpiresAt;
        var actor = Guid.NewGuid();
        var occurredAt = Start.AddDays(2);

        license.Renew(previousExpiration.AddMonths(1), actor, occurredAt);

        var change = license.Changes[1];
        Assert.Equal(LicenseChangeKind.Renewed, change.Kind);
        Assert.Equal(actor, change.ActorId);
        Assert.Equal(occurredAt, change.OccurredAt);
        Assert.Equal(previousExpiration, change.PreviousExpiresAt);
        Assert.Equal(previousExpiration.AddMonths(1), change.NewExpiresAt);
        Assert.Equal(change.PreviousStartsAt, change.NewStartsAt);
        Assert.Equal(change.PreviousMaxBranches, change.NewMaxBranches);
        Assert.True(license.AllowsOperation(previousExpiration.AddDays(1)));
    }

    [Fact]
    public void RenewalCannotReduceExpirationOrAppendFailedChange()
    {
        var license = CreateTenant().License;
        var expiration = license.ExpiresAt;

        Assert.Throws<ArgumentException>(() => license.Renew(expiration.AddTicks(-1), Actor, Start));

        Assert.Equal(expiration, license.ExpiresAt);
        Assert.Single(license.Changes);
    }

    [Fact]
    public void UnchangedRenewalIsANoOp()
    {
        var license = CreateTenant().License;
        license.Renew(license.ExpiresAt, Actor, Start);
        Assert.Single(license.Changes);
    }

    [Theory]
    [InlineData(LicenseStatus.Cancelled)]
    [InlineData(LicenseStatus.PurgePending)]
    [InlineData(LicenseStatus.Purged)]
    public void RenewalCannotChangeCancelledOrPurgeStates(LicenseStatus status)
    {
        var license = CreateTenant(status).License;
        Assert.Throws<InvalidOperationException>(() => license.Renew(license.ExpiresAt.AddMonths(1), Actor, Start));
        Assert.Single(license.Changes);
    }

    [Theory]
    [InlineData(LicenseStatus.Trial)]
    [InlineData(LicenseStatus.Active)]
    [InlineData(LicenseStatus.Grace)]
    public void SuspensionBlocksAnOtherwiseValidLicense(LicenseStatus status)
    {
        var license = CreateTenant(status).License;
        license.Suspend(Actor, Start);

        Assert.False(license.AllowsOperation(Start));
        Assert.Equal(status, license.Changes[1].PreviousStatus);
        Assert.Equal(LicenseStatus.Suspended, license.Changes[1].NewStatus);
    }

    [Fact]
    public void RenewalDoesNotReactivateSuspendedLicense()
    {
        var license = CreateTenant().License;
        license.Suspend(Actor, Start);
        license.Renew(license.ExpiresAt.AddMonths(1), Actor, Start);

        Assert.Equal(LicenseStatus.Suspended, license.Status);
        Assert.False(license.AllowsOperation(Start.AddMonths(1)));
    }

    [Theory]
    [InlineData(LicenseStatus.Trial)]
    [InlineData(LicenseStatus.Active)]
    [InlineData(LicenseStatus.Grace)]
    public void ReactivationRestoresSelectedOperationalStateWhenPeriodIsValid(LicenseStatus status)
    {
        var license = CreateTenant().License;
        license.Suspend(Actor, Start);
        license.Reactivate(status, Actor, Start.AddDays(1));

        Assert.True(license.AllowsOperation(Start.AddDays(1)));
        Assert.Equal(status, license.Status);
        Assert.Equal(LicenseChangeKind.Reactivated, license.Changes[2].Kind);
        Assert.Equal(LicenseStatus.Suspended, license.Changes[2].PreviousStatus);
    }

    [Theory]
    [InlineData(LicenseStatus.Trial)]
    [InlineData(LicenseStatus.Active)]
    [InlineData(LicenseStatus.Grace)]
    [InlineData(LicenseStatus.Cancelled)]
    [InlineData(LicenseStatus.PurgePending)]
    [InlineData(LicenseStatus.Purged)]
    public void ReactivationRequiresSuspendedLicense(LicenseStatus status)
    {
        var license = CreateTenant(status).License;
        Assert.Throws<InvalidOperationException>(() => license.Reactivate(LicenseStatus.Active, Actor, Start));
        Assert.Single(license.Changes);
    }

    [Theory]
    [InlineData(LicenseStatus.Suspended)]
    [InlineData(LicenseStatus.Cancelled)]
    [InlineData(LicenseStatus.PurgePending)]
    [InlineData(LicenseStatus.Purged)]
    public void ReactivationRejectsNonOperationalTarget(LicenseStatus target)
    {
        var license = CreateTenant(LicenseStatus.Suspended).License;
        Assert.Throws<ArgumentOutOfRangeException>(() => license.Reactivate(target, Actor, Start));
        Assert.Single(license.Changes);
    }

    [Fact]
    public void ReactivationDoesNotBypassExpiration()
    {
        var license = CreateTenant(LicenseStatus.Suspended).License;
        license.Reactivate(LicenseStatus.Active, Actor, license.ExpiresAt);
        Assert.False(license.AllowsOperation(license.ExpiresAt));
    }

    [Fact]
    public void RequestPurgeBlocksOperationAndPreservesLifecycleHistory()
    {
        var license = CreateTenant().License;
        license.Renew(license.ExpiresAt.AddMonths(1), Actor, Start.AddMinutes(1));
        license.Suspend(Actor, Start.AddMinutes(2));
        license.Reactivate(LicenseStatus.Active, Actor, Start.AddMinutes(3));
        license.RequestPurge(Actor, Start.AddMinutes(4));

        Assert.Equal(LicenseStatus.PurgePending, license.Status);
        Assert.False(license.AllowsOperation(Start.AddDays(1)));
        Assert.Equal(new[] { LicenseChangeKind.Created, LicenseChangeKind.Renewed, LicenseChangeKind.Suspended,
            LicenseChangeKind.Reactivated, LicenseChangeKind.PurgeRequested }, license.Changes.Select(change => change.Kind));
        Assert.All(license.Changes, change =>
        {
            Assert.Equal(license.Id, change.LicenseId);
            Assert.Equal(license.TenantId, change.TenantId);
            Assert.Equal(Actor, change.ActorId);
            Assert.Equal(TimeSpan.Zero, change.OccurredAt.Offset);
        });
        Assert.Equal(LicenseStatus.Active, license.Changes[4].PreviousStatus);
        Assert.Equal(LicenseStatus.PurgePending, license.Changes[4].NewStatus);
    }

    [Fact]
    public void DuplicateSuspensionAndPurgeRequestsDoNotAppendChanges()
    {
        var license = CreateTenant().License;
        license.Suspend(Actor, Start);
        license.Suspend(Actor, Start);
        license.RequestPurge(Actor, Start);
        license.RequestPurge(Actor, Start);
        Assert.Equal(new[] { LicenseChangeKind.Created, LicenseChangeKind.Suspended, LicenseChangeKind.PurgeRequested },
            license.Changes.Select(change => change.Kind));
    }

    [Fact]
    public void CancelledTenantCanRequestPurgeButPurgedTenantCannot()
    {
        var cancelled = CreateTenant(LicenseStatus.Cancelled).License;
        cancelled.RequestPurge(Actor, Start);
        Assert.Equal(LicenseStatus.PurgePending, cancelled.Status);

        var purged = CreateTenant(LicenseStatus.Purged).License;
        Assert.Throws<InvalidOperationException>(() => purged.RequestPurge(Actor, Start));
        Assert.Single(purged.Changes);
    }

    [Fact]
    public void MissingActorCannotMutateLicenseAndHistoryCollectionIsReadOnly()
    {
        var license = CreateTenant().License;
        Assert.Throws<ArgumentException>(() => license.Suspend(Guid.Empty, Start));
        Assert.True(license.AllowsOperation(Start));
        Assert.Single(license.Changes);

        var collection = Assert.IsAssignableFrom<ICollection<LicenseChange>>(license.Changes);
        Assert.True(collection.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => collection.Clear());
    }

    private static Tenant CreateTenant(LicenseStatus status = LicenseStatus.Active) =>
        Tenant.Create(" Botica ", Start, Start.AddMonths(1), 3, status, Actor, Start);
}
