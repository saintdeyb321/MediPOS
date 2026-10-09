using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Commissions;
using MediPOS.Application.Modules.Commissions.DeactivateCommissionRule;
using MediPOS.Application.Modules.Commissions.GetCommissionRules;
using MediPOS.Application.Modules.Commissions.SetCommissionRule;
using MediPOS.Application.Modules.Commissions.SetTenantCommissionsEnabled;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Commissions;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.UnitTests.Modules.Commissions;

public sealed class CommissionConfigurationUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 15, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnerInitializesDefaultDisabledOrEnablesWithServerIdentityAndAtomicAudit(bool enabled)
    {
        var setup = new Setup();
        var result = await setup.Settings.HandleAsync(new(setup.TenantId, enabled), TestContext.Current.CancellationToken);
        Assert.Equal(enabled, result.IsEnabled);
        Assert.Equal(setup.Member.UserId, result.UpdatedByActorId);
        Assert.Equal(Now, result.UpdatedAt);
        var audit = Assert.Single(setup.Store.Scope.Audits);
        Assert.Equal((setup.TenantId, setup.Member.UserId, setup.TenantId, AuditAction.CommissionSettingsChanged),
            (audit.TenantId, audit.ActorId, audit.EntityId, audit.Action));
        Assert.Null(audit.BeforeJson);
        using var snapshot = JsonDocument.Parse(audit.AfterJson!);
        Assert.Equal(enabled, snapshot.RootElement.GetProperty("isEnabled").GetBoolean());
        Assert.True(setup.Store.Scope.Committed);
        Assert.Equal(3, setup.Access.Calls);
    }

    [Fact]
    public async Task DisablingSettingsDoesNotRewriteExistingRuleHistoryAndNoOpDoesNotAuditAgain()
    {
        var setup = new Setup();
        var settings = TenantCommissionSettings.Create(setup.TenantId, setup.Member.UserId, Now.AddDays(-1));
        settings.SetEnabled(true, setup.Member.UserId, Now.AddHours(-1));
        setup.Store.Scope.Settings = settings;
        var historical = setup.Rule(0.2m);
        setup.Store.Scope.Rule = historical;
        await setup.Settings.HandleAsync(new(setup.TenantId, false), TestContext.Current.CancellationToken);
        Assert.False(settings.IsEnabled);
        Assert.True(historical.IsActive);
        Assert.Equal(0.2m, historical.Value);
        var audit = Assert.Single(setup.Store.Scope.Audits);
        using var before = JsonDocument.Parse(audit.BeforeJson!);
        Assert.True(before.RootElement.GetProperty("isEnabled").GetBoolean());
        setup.Store.Scope.ResetCommit();
        await setup.Settings.HandleAsync(new(setup.TenantId, false), TestContext.Current.CancellationToken);
        Assert.Single(setup.Store.Scope.Audits);
    }

    [Theory]
    [InlineData("cashier", "commissions.forbidden")]
    [InlineData("pharmacist", "commissions.forbidden")]
    [InlineData("license_missing", "access.license_denied")]
    [InlineData("license_suspended", "access.license_denied")]
    [InlineData("membership_missing", "access.membership_missing")]
    [InlineData("membership_inactive", "access.membership_inactive")]
    [InlineData("identity_missing", "access.unauthenticated")]
    [InlineData("foreign_tenant", "access.tenant_mismatch")]
    public async Task AllConfigurationCasesDenyBeforeReadingOrWritingPrivateConfiguration(string scenario, string code)
    {
        var setup = new Setup();
        setup.Deny(scenario);
        await ErrorAsync(code, () => setup.Settings.HandleAsync(new(setup.TenantId, true), TestContext.Current.CancellationToken));
        await ErrorAsync(code, () => setup.SetRule.HandleAsync(setup.Command(), TestContext.Current.CancellationToken));
        await ErrorAsync(code, () => setup.Deactivate.HandleAsync(new(setup.TenantId, Guid.NewGuid()), TestContext.Current.CancellationToken));
        await ErrorAsync(code, () => setup.Get.HandleAsync(new(setup.TenantId), TestContext.Current.CancellationToken));
        Assert.Equal(0, setup.Store.Begins);
        Assert.Equal(0, setup.Store.Reads);
        Assert.Empty(setup.Store.Scope.Audits);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForeignOrInactiveProductCannotGetARule(bool foreign)
    {
        var setup = new Setup();
        if (foreign)
        {
            setup.Store.Scope.Product = BusinessProduct.CreateLocal(Guid.NewGuid(), "OTHER", ProductType.Retail, "Other",
                Guid.NewGuid(), "Brand", null, null, 1m, null, Now);
        }
        else setup.Store.Scope.Product!.SetStatus(false);
        await ErrorAsync("commissions.product_not_found", () => setup.SetRule.HandleAsync(setup.Command(), TestContext.Current.CancellationToken));
        Assert.Null(setup.Store.Scope.Replacement);
        Assert.Empty(setup.Store.Scope.Audits);
        Assert.True(setup.Store.Scope.RolledBack);
    }

    [Theory]
    [InlineData(CommissionRuleType.Fixed, "0")]
    [InlineData(CommissionRuleType.Fixed, "0.00001")]
    [InlineData(CommissionRuleType.Percentage, "100.0001")]
    public async Task InvalidInputsCannotDeactivateThePreviousVersion(CommissionRuleType type, string value)
    {
        var setup = new Setup();
        var previous = setup.Rule(0.2m);
        setup.Store.Scope.Rule = previous;
        await ErrorAsync("commissions.invalid_rule", () => setup.SetRule.HandleAsync(setup.Command() with
        {
            RuleType = type,
            Value = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture),
        }, TestContext.Current.CancellationToken));
        Assert.True(previous.IsActive);
        Assert.Empty(setup.Store.Scope.Audits);
    }

    [Fact]
    public async Task ReplacingRulePreservesItsHistoricalValuesAndAuditsBothVersionsInOneScope()
    {
        var setup = new Setup();
        var previous = setup.Rule(0.2m);
        setup.Store.Scope.Rule = previous;
        var result = await setup.SetRule.HandleAsync(setup.Command() with { RuleType = CommissionRuleType.Percentage, Value = 5m }, TestContext.Current.CancellationToken);
        Assert.NotEqual(previous.Id, result.Id);
        Assert.False(previous.IsActive);
        Assert.Equal((CommissionRuleType.Fixed, 0.2m, Now.AddDays(-1)), (previous.RuleType, previous.Value, previous.CreatedAt));
        Assert.Equal((setup.Member.UserId, Now), (previous.DeactivatedByActorId!.Value, previous.DeactivatedAt!.Value));
        Assert.Equal((CommissionRuleType.Percentage, 5m, true), (result.RuleType, result.Value, result.IsActive));
        Assert.Same(previous, setup.Store.Scope.Previous);
        Assert.Equal([AuditAction.CommissionRuleDeactivated, AuditAction.CommissionRuleCreated], setup.Store.Scope.Audits.Select(audit => audit.Action));
        Assert.All(setup.Store.Scope.Audits, audit => Assert.Equal(setup.Member.UserId, audit.ActorId));
        Assert.Equal(1, setup.Store.Begins);
        Assert.True(setup.Store.Scope.Committed);
    }

    [Fact]
    public async Task DeactivationPreservesValuesAndAlreadyInactiveRuleIsIdempotent()
    {
        var setup = new Setup();
        var rule = setup.Rule(0.2m);
        setup.Store.Scope.Rule = rule;
        var result = await setup.Deactivate.HandleAsync(new(setup.TenantId, rule.Id), TestContext.Current.CancellationToken);
        Assert.False(result.IsActive);
        Assert.Equal(0.2m, result.Value);
        Assert.Equal(AuditAction.CommissionRuleDeactivated, Assert.Single(setup.Store.Scope.Audits).Action);
        setup.Store.Scope.ResetCommit();
        await setup.Deactivate.HandleAsync(new(setup.TenantId, rule.Id), TestContext.Current.CancellationToken);
        Assert.Single(setup.Store.Scope.Audits);
    }

    [Fact]
    public async Task OwnerRevokedDuringMutationCannotCommitAndScopedWritesRollback()
    {
        var setup = new Setup();
        setup.Store.Scope.AfterPersist = () => setup.Access.Snapshot = setup.Access.Snapshot with { License = null };
        await ErrorAsync("access.license_denied", () => setup.Settings.HandleAsync(new(setup.TenantId, true), TestContext.Current.CancellationToken));
        Assert.False(setup.Store.Scope.Committed);
        Assert.True(setup.Store.Scope.RolledBack);
    }

    [Fact]
    public async Task HistoricalListingPassesBoundedFiltersAndRechecksOwnerBeforeReturning()
    {
        var setup = new Setup();
        var historical = setup.Rule(0.2m);
        historical.Deactivate(setup.Member.UserId, Now);
        setup.Store.Rows = [historical];
        var query = new GetCommissionRulesQuery(setup.TenantId, setup.ProductId, true, 25, 30);
        var result = Assert.Single(await setup.Get.HandleAsync(query, TestContext.Current.CancellationToken));
        Assert.Equal(historical.Id, result.Id);
        Assert.False(result.IsActive);
        Assert.Equal(query, setup.Store.Query);
        Assert.Equal(2, setup.Access.Calls);
        setup.Store.AfterRead = () => setup.Member.Deactivate(Now);
        await ErrorAsync("access.membership_inactive", () => setup.Get.HandleAsync(query, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ForeignRuleReturnedByReadBoundaryNeverEscapesApplication()
    {
        var setup = new Setup();
        setup.Store.Rows = [CommissionRule.Create(Guid.NewGuid(), setup.ProductId, CommissionRuleType.Fixed, 1m, Now, null, setup.Member.UserId, Now)];
        await ErrorAsync("tenant.scope_conflict", () => setup.Get.HandleAsync(new(setup.TenantId), TestContext.Current.CancellationToken));
    }

    private static async Task ErrorAsync(string code, Func<Task> action) =>
        Assert.Equal(code, (await Assert.ThrowsAsync<ApplicationErrorException>(action)).Error.Code);

    private sealed class Setup
    {
        public Setup()
        {
            Member = Membership.Create(Guid.NewGuid(), Guid.NewGuid(), TenantRole.Owner, Now.AddDays(-1));
            Identity = new() { UserId = Member.UserId };
            Access = new(new(true, Member, License.Create(TenantId, Now.AddDays(-1), Now.AddMonths(1), 3, LicenseStatus.Active, Member.UserId, Now.AddDays(-1)), false, false, []));
            var resolver = new ResolveAccessContextHandler(Identity, Access, new TenantDataContext());
            Store = new(new(TenantId, BusinessProduct.CreateLocal(TenantId, "LOCAL", ProductType.Retail, "Product", Guid.NewGuid(), "Brand", null, null, 1m, null, Now.AddDays(-1))));
            Settings = new(Store, resolver, new Clock()); SetRule = new(Store, resolver, new Clock());
            Deactivate = new(Store, resolver, new Clock()); Get = new(Store, resolver, new Clock());
        }
        public Guid TenantId => Member.TenantId;
        public Guid ProductId => Store.Scope.Product!.Id;
        public Membership Member { get; }
        public Identity Identity { get; }
        public AccessReader Access { get; }
        public Store Store { get; }
        public SetTenantCommissionsEnabledHandler Settings { get; }
        public SetCommissionRuleHandler SetRule { get; }
        public DeactivateCommissionRuleHandler Deactivate { get; }
        public GetCommissionRulesHandler Get { get; }
        public SetCommissionRuleCommand Command() => new(TenantId, ProductId, CommissionRuleType.Fixed, 0.3m, Now, null);
        public CommissionRule Rule(decimal value) => CommissionRule.Create(TenantId, ProductId, CommissionRuleType.Fixed, value, Now.AddDays(-1), null, Member.UserId, Now.AddDays(-1));
        public void Deny(string scenario)
        {
            switch (scenario)
            {
                case "cashier": Access.Snapshot = Access.Snapshot with { Membership = Membership.Create(TenantId, Member.UserId, TenantRole.Cashier, Now) }; break;
                case "pharmacist": Access.Snapshot = Access.Snapshot with { Membership = Membership.Create(TenantId, Member.UserId, TenantRole.Pharmacist, Now) }; break;
                case "license_missing": Access.Snapshot = Access.Snapshot with { License = null }; break;
                case "license_suspended": Access.Snapshot.License!.Suspend(Member.UserId, Now); break;
                case "membership_missing": Access.Snapshot = Access.Snapshot with { Membership = null }; break;
                case "membership_inactive": Member.Deactivate(Now); break;
                case "identity_missing": Identity.UserId = null; break;
                case "foreign_tenant": Access.Snapshot = Access.Snapshot with { Membership = Membership.Create(Guid.NewGuid(), Member.UserId, TenantRole.Owner, Now) }; break;
                default: throw new ArgumentOutOfRangeException(nameof(scenario));
            }
        }
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Identity : IAuthenticatedMediPosUser { public Guid? UserId { get; set; } }
    private sealed class AccessReader(OperationalAccessSnapshot snapshot) : IOperationalAccessReader
    {
        public OperationalAccessSnapshot Snapshot { get; set; } = snapshot;
        public int Calls { get; private set; }
        public Task<OperationalAccessSnapshot> ReadAsync(Guid userId, Guid tenantId, Guid? branchId, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(Snapshot); }
    }
    private sealed class Store(Scope scope) : ICommissionConfigurationStore
    {
        public Scope Scope { get; } = scope;
        public int Begins { get; private set; }
        public int Reads { get; private set; }
        public GetCommissionRulesQuery? Query { get; private set; }
        public IReadOnlyList<CommissionRule> Rows { get; set; } = [];
        public Action? AfterRead { get; set; }
        public Task<ICommissionConfigurationScope> BeginAsync(Guid tenantId, CancellationToken cancellationToken)
        { Begins++; return Task.FromResult<ICommissionConfigurationScope>(Scope); }
        public Task<IReadOnlyList<CommissionRule>> ReadRulesAsync(Guid tenantId, Guid? productId, bool includeInactive, int offset, int limit, CancellationToken cancellationToken)
        {
            Reads++; Query = new(tenantId, productId, includeInactive, offset, limit); AfterRead?.Invoke();
            return Task.FromResult(Rows);
        }
    }
    private sealed class Scope(Guid tenantId, BusinessProduct product) : ICommissionConfigurationScope
    {
        public Guid TenantId => tenantId;
        public TenantCommissionSettings? Settings { get; set; }
        public BusinessProduct? Product { get; set; } = product;
        public CommissionRule? Rule { get; set; }
        public CommissionRule? Previous { get; private set; }
        public CommissionRule? Replacement { get; private set; }
        public List<AuditLog> Audits { get; } = [];
        public bool Committed { get; private set; }
        public bool RolledBack { get; private set; }
        public Action? AfterPersist { get; set; }
        public Task<TenantCommissionSettings?> LoadSettingsAsync(CancellationToken cancellationToken) => Task.FromResult(Settings);
        public Task<BusinessProduct?> LoadProductAsync(Guid productId, CancellationToken cancellationToken) => Task.FromResult(Product);
        public Task<CommissionRule?> LoadActiveRuleAsync(Guid productId, CancellationToken cancellationToken) => Task.FromResult(Rule is { IsActive: true } ? Rule : null);
        public Task<CommissionRule?> LoadRuleAsync(Guid ruleId, CancellationToken cancellationToken) => Task.FromResult(Rule);
        public Task PersistSettingsAsync(TenantCommissionSettings settings, AuditLog audit, CancellationToken cancellationToken)
        { Settings = settings; Audits.Add(audit); AfterPersist?.Invoke(); return Task.CompletedTask; }
        public Task ReplaceRuleAsync(CommissionRule? previous, CommissionRule replacement, AuditLog? deactivationAudit, AuditLog creationAudit, CancellationToken cancellationToken)
        {
            Previous = previous; Replacement = replacement;
            if (deactivationAudit is not null) Audits.Add(deactivationAudit);
            Audits.Add(creationAudit); AfterPersist?.Invoke(); return Task.CompletedTask;
        }
        public Task PersistDeactivationAsync(CommissionRule rule, AuditLog audit, CancellationToken cancellationToken)
        { Audits.Add(audit); AfterPersist?.Invoke(); return Task.CompletedTask; }
        public Task CompleteAsync(CancellationToken cancellationToken) { Committed = true; return Task.CompletedTask; }
        public ValueTask DisposeAsync() { RolledBack = !Committed; return ValueTask.CompletedTask; }
        public void ResetCommit() => Committed = false;
    }
}
