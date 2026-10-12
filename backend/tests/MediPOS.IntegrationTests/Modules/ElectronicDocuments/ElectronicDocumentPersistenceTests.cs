using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.ElectronicDocuments;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.ElectronicDocuments;
using MediPOS.Infrastructure;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.SalesPos;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using MediPOS.IntegrationTests.Tenancy;
using MediPOS.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MediPOS.IntegrationTests.Modules.ElectronicDocuments;

[Collection("PostgreSQL")]
[Trait("Category", "PostgreSql")]
public sealed class ElectronicDocumentPersistenceTests(PostgreSqlFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    [Fact]
    public async Task MigrationMatchesModelAndAllPrivateTablesHaveForceRlsAndCompositeForeignKeys()
    {
        await using var context = fixture.CreateConstraintContext(); Assert.False(context.Database.HasPendingModelChanges());
        Assert.Contains(await context.Database.GetAppliedMigrationsAsync(Token), v => v.EndsWith("_AddElectronicDocuments", StringComparison.Ordinal));
        Assert.Equal(3, await context.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_class WHERE relname IN ('electronic_documents','electronic_document_series','electronic_document_attempts') AND relrowsecurity AND relforcerowsecurity").SingleAsync(Token));
        Assert.Equal(9, await context.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_constraint WHERE contype='f' AND conrelid IN ('electronic_documents'::regclass,'electronic_document_series'::regclass,'electronic_document_attempts'::regclass) AND cardinality(conkey)=2").SingleAsync(Token));
        Assert.Equal(3, await context.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_policies WHERE tablename IN ('electronic_documents','electronic_document_series','electronic_document_attempts') AND qual IS NOT NULL AND with_check IS NOT NULL").SingleAsync(Token));
    }
    [Fact]
    public async Task ConfirmedSaleRequestsDurablyAndDuplicateReturnsSameNumberWithoutCommercialMutation()
    {
        await using var services = Services(); var setup = await SetupAsync(services);
        var before = await CommercialAsync(setup.Command.TenantId);
        var id = await RequestAsync(services, setup); Assert.Equal(id, await RequestAsync(services, setup));
        await using var context = fixture.CreateContext(setup.Command.TenantId);
        var doc = await context.ElectronicDocuments.SingleAsync(Token); Assert.Equal(id, doc.Id); Assert.Equal("pending", doc.Status);
        Assert.Equal(2, (await context.ElectronicDocumentSeries.SingleAsync(Token)).NextNumber); Assert.Equal("TEST1-1", doc.DocumentNumber);
        Assert.Equal(before, await CommercialAsync(setup.Command.TenantId));
        Assert.Equal(1, await context.AuditLogs.CountAsync(v => v.EntityId == id && v.Action == MediPOS.Domain.Modules.AuditSupport.AuditAction.ElectronicDocumentRequested, Token));
        using var snapshot = JsonDocument.Parse(doc.SnapshotJson); Assert.Equal(2.125m, snapshot.RootElement.GetProperty("sale").GetProperty("lines")[0].GetProperty("UnitPriceSnapshot").GetDecimal());
    }
    [Fact]
    public async Task ConcurrentEquivalentRequestsCreateOneIdentityAndConflictingRequestCannotAllocate()
    {
        await using var services = Services(); var setup = await SetupAsync(services);
        var result = await Task.WhenAll(RequestAsync(services, setup), RequestAsync(services, setup)); Assert.Equal(result[0], result[1]);
        var changed = setup with { Command = setup.Command with { Fiscal = setup.Command.Fiscal with { Customer = setup.Command.Fiscal.Customer with { Name = "Different" } } } };
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => RequestAsync(services, changed)); Assert.Equal("cpe.request_conflict", error.Error.Code);
        await using var context = fixture.CreateContext(setup.Command.TenantId); Assert.Equal(1, await context.ElectronicDocuments.CountAsync(Token)); Assert.Equal(2, (await context.ElectronicDocumentSeries.SingleAsync(Token)).NextNumber);
    }
    [Fact]
    public async Task TwoDifferentSalesReserveDistinctSequentialNumbersConcurrently()
    {
        await using var services = Services(); var setup = await SetupAsync(services);
        await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        CashSessionTestData.Authenticate(source, setup.Rows.Tenant.Identity.UserId);
        var draft = await source.GetRequiredService<MediPOS.Application.Modules.SalesPos.CreateSaleDraft.CreateSaleDraftHandler>().HandleAsync(new(setup.Command.TenantId, setup.Command.BranchId), Token);
        var updated = await source.GetRequiredService<MediPOS.Application.Modules.SalesPos.ReplaceSaleLines.ReplaceSaleLinesHandler>().HandleAsync(new(setup.Command.TenantId, setup.Command.BranchId, draft.SaleId, draft.Version, [new(setup.Rows.ProductId, setup.Rows.UnitId, 1m, MediPOS.Domain.Modules.SalesPos.PriceKind.Retail)]), Token);
        var otherRows = setup.Rows with { Draft = updated }; await SaleCheckoutTestData.ConfirmAsync(source, otherRows);
        var otherCommand = await CommandAsync(source, otherRows, setup.Command.SeriesId);
        var other = setup with { Rows = otherRows, Command = otherCommand };
        await Task.WhenAll(RequestAsync(services, setup), RequestAsync(services, other));
        await using var context = fixture.CreateContext(setup.Command.TenantId);
        Assert.Equal(new long[] { 1, 2 }, await context.ElectronicDocuments.OrderBy(v => v.SequentialNumber).Select(v => v.SequentialNumber).ToArrayAsync(Token));
        Assert.Equal(3, (await context.ElectronicDocumentSeries.SingleAsync(Token)).NextNumber);
    }
    [Fact]
    public async Task SavedRequestAndNumberRollBackTogetherWithAuditBeforeCommit()
    {
        await using var services = Services(); var setup = await SetupAsync(services);
        await using (var scope = services.CreateAsyncScope())
        {
            var source = scope.ServiceProvider; var provisioning = source.GetRequiredService<ITenantLicenseProvisioning>();
            await using var transaction = await provisioning.BeginAsync(setup.Command.TenantId, Token); Assert.NotNull(transaction);
            var store = source.GetRequiredService<IElectronicDocumentStore>(); var data = await store.LoadRequestAsync(setup.Command, Token); Assert.NotNull(data);
            var doc = ElectronicDocument.Create(data.Series, data.Branch.Id, data.Sale.Id, data.Sale.SellerMembershipId, data.Sale.SellerMembershipId,
                ElectronicDocumentPreparation.Snapshot(data, setup.Command.Fiscal), ElectronicDocumentPreparation.Fingerprint(setup.Command), "PEN", data.Sale.TotalAmount, ElectronicDocumentTestData.Now);
            await store.SaveRequestAsync(doc, setup.Rows.Tenant.Identity.UserId, Token);
            // Simulate interruption after SQL writes, before commit. The licensed scope rolls back and clears tracking.
        }
        await using var context = fixture.CreateContext(setup.Command.TenantId);
        Assert.Empty(await context.ElectronicDocuments.ToArrayAsync(Token)); Assert.Equal(1, (await context.ElectronicDocumentSeries.SingleAsync(Token)).NextNumber);
        Assert.False(await context.AuditLogs.AnyAsync(v => v.Action == MediPOS.Domain.Modules.AuditSupport.AuditAction.ElectronicDocumentRequested, Token));
        Assert.NotEqual(Guid.Empty, await RequestAsync(services, setup));
    }
    [Fact]
    public async Task TwoWorkersSkipLockedClaimsAndReleaseNeverDuplicatesDispatch()
    {
        await using var services = Services(); var setup = await SetupAsync(services); var id = await RequestAsync(services, setup);
        var identity = await services.GetRequiredService<IElectronicDeliveryIdentity>().AuthorizeAsync(setup.Command.TenantId, Token); Assert.NotNull(identity);
        await using var locked = fixture.CreateContext(setup.Command.TenantId); await using var tx = await locked.Database.BeginTransactionAsync(Token);
        await locked.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM electronic_documents WHERE id={id} FOR UPDATE", Token);
        await using var scope = services.CreateAsyncScope(); var queue = scope.ServiceProvider.GetRequiredService<IElectronicDeliveryWorkStore>();
        Assert.Null(await queue.ClaimAsync(identity, ElectronicDocumentTestData.Now, TimeSpan.FromMinutes(1), Token));
        await tx.RollbackAsync(Token);
        var claims = await Task.WhenAll(ClaimAsync(services, identity), ClaimAsync(services, identity)); Assert.Single(claims, v => v is not null);
    }
    [Fact]
    public async Task RestartRecoversUnsentClaimButPostDispatchTimeoutBecomesUncertainAndLateResponseIsFenced()
    {
        var clock = new ElectronicDocumentTestData.Clock(); await using var services = Services(clock); var setup = await SetupAsync(services); await RequestAsync(services, setup);
        var identity = await services.GetRequiredService<IElectronicDeliveryIdentity>().AuthorizeAsync(setup.Command.TenantId, Token); Assert.NotNull(identity);
        var first = await ClaimAsync(services, identity); Assert.NotNull(first); clock.NowUtc = clock.NowUtc.AddMinutes(1);
        await using (var restart = services.CreateAsyncScope()) Assert.Equal(1, await restart.ServiceProvider.GetRequiredService<IElectronicDeliveryWorkStore>().RecoverExpiredAsync(identity, clock.NowUtc, 10, Token));
        clock.NowUtc = clock.NowUtc.AddSeconds(30); var next = await ClaimAsync(services, identity); Assert.NotNull(next); Assert.NotEqual(first.Token, next.Token); Assert.Equal(first.IdempotencyKey, next.IdempotencyKey);
        await using (var dispatch = services.CreateAsyncScope()) Assert.True(await dispatch.ServiceProvider.GetRequiredService<IElectronicDeliveryWorkStore>().BeginDispatchAsync(identity, next, clock.NowUtc, Token));
        clock.NowUtc = clock.NowUtc.AddMinutes(1);
        await using (var restart = services.CreateAsyncScope())
        {
            var queue = restart.ServiceProvider.GetRequiredService<IElectronicDeliveryWorkStore>(); Assert.Equal(1, await queue.RecoverExpiredAsync(identity, clock.NowUtc, 10, Token));
            Assert.False(await queue.CompleteAsync(identity, next, new(DeliveryOutcome.Received, "late", "R"), clock.NowUtc, Token)); Assert.Null(await queue.ClaimAsync(identity, clock.NowUtc, TimeSpan.FromMinutes(1), Token));
        }
        await using var context = fixture.CreateContext(setup.Command.TenantId); Assert.Equal("uncertain", (await context.ElectronicDocuments.SingleAsync(Token)).Status);
        Assert.Equal(4, await context.ElectronicDocumentAttempts.CountAsync(Token));
    }
    [Theory]
    [InlineData(DeliveryOutcome.Received, "simulated")]
    [InlineData(DeliveryOutcome.AuthorityAccepted, "simulated")]
    [InlineData(DeliveryOutcome.Rejected, "simulated")]
    [InlineData(DeliveryOutcome.TemporaryNotReceived, "simulated")]
    [InlineData(DeliveryOutcome.Indeterminate, "simulated")]
    public async Task TypedProviderResultPersistsAcrossNewWorkerScopeWithoutCommercialWrites(DeliveryOutcome outcome, string expected)
    {
        var provider = new ElectronicDocumentTestData.Provider(new(outcome, "result", "R")); await using var services = Services(provider: provider);
        var setup = await SetupAsync(services); await RequestAsync(services, setup); var before = await CommercialAsync(setup.Command.TenantId);
        await using (var scope = services.CreateAsyncScope()) Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<RunElectronicDocumentDelivery>().HandleAsync(setup.Command.TenantId, 10, Token));
        await using var context = fixture.CreateContext(setup.Command.TenantId); var doc = await context.ElectronicDocuments.SingleAsync(Token);
        Assert.Equal(expected, doc.Status); Assert.Equal(2, await context.ElectronicDocumentAttempts.CountAsync(Token)); Assert.Equal(before, await CommercialAsync(setup.Command.TenantId));
        Assert.Equal("simulation", doc.ProviderStatus); Assert.Null(doc.EvidenceSha256); Assert.Null(doc.NextAttemptAtUtc);
        Assert.StartsWith("simulation.", doc.LastErrorCode!, StringComparison.Ordinal);
        var completion = await context.ElectronicDocumentAttempts.SingleAsync(v => v.Phase == "completed", Token);
        Assert.Equal("simulated", completion.Outcome); Assert.Equal(doc.LastErrorCode, completion.ErrorCode);
        await using (var restart = services.CreateAsyncScope()) Assert.Equal(0, await restart.ServiceProvider.GetRequiredService<RunElectronicDocumentDelivery>().HandleAsync(setup.Command.TenantId, 10, Token));
        Assert.Equal(1, provider.Calls);
    }
    [Fact]
    public async Task DisabledProviderPreservesPendingAndExpiredLicenseCannotDispatchClaim()
    {
        var clock = new ElectronicDocumentTestData.Clock(); await using var services = Services(clock); var setup = await SetupAsync(services); await RequestAsync(services, setup);
        await using (var scope = services.CreateAsyncScope()) Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<RunElectronicDocumentDelivery>().HandleAsync(setup.Command.TenantId, 1, Token));
        await using (var context = fixture.CreateContext(setup.Command.TenantId)) Assert.Equal("pending", (await context.ElectronicDocuments.SingleAsync(Token)).Status);
        var identity = await services.GetRequiredService<IElectronicDeliveryIdentity>().AuthorizeAsync(setup.Command.TenantId, Token); Assert.NotNull(identity);
        var claim = await ClaimAsync(services, identity); Assert.NotNull(claim); clock.NowUtc = clock.NowUtc.AddMonths(2);
        await using var restart = services.CreateAsyncScope(); var queue = restart.ServiceProvider.GetRequiredService<IElectronicDeliveryWorkStore>();
        Assert.False(await queue.BeginDispatchAsync(identity, claim, clock.NowUtc, Token)); await queue.RecoverExpiredAsync(identity, clock.NowUtc, 10, Token);
        Assert.Null(await queue.ClaimAsync(identity, clock.NowUtc.AddDays(1), TimeSpan.FromMinutes(1), Token));
        await using var verify = fixture.CreateContext(setup.Command.TenantId); var doc = await verify.ElectronicDocuments.SingleAsync(Token); Assert.Null(doc.SubmittedAtUtc); Assert.Equal("retry_wait", doc.Status);
    }
    [Fact]
    public async Task CashierCanQueryOwnDocumentButOwnerOnlySeriesAdministrationAndVersionAreEnforced()
    {
        await using var services = Services(); var setup = await SetupAsync(services); var id = await RequestAsync(services, setup);
        await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider; CashSessionTestData.Authenticate(source, setup.Rows.Tenant.Identity.UserId);
        var query = source.GetRequiredService<QueryElectronicDocumentsHandler>(); Assert.Equal(id, Assert.Single((await query.HandleAsync(new(setup.Command.TenantId, setup.Command.BranchId, Id: id), Token)).Items).Id);
        await Assert.ThrowsAsync<ApplicationErrorException>(() => query.SeriesAsync(setup.Command.TenantId, setup.Command.BranchId, 0, 50, Token));
        var context = source.GetRequiredService<MediPosDbContext>(); var owner = await context.Memberships.AsNoTracking().Where(v => v.Role == MediPOS.Domain.Modules.IdentityAccess.TenantRole.Owner).Select(v => v.UserId).SingleAsync(Token);
        CashSessionTestData.Authenticate(source, owner); var series = Assert.Single((await query.SeriesAsync(setup.Command.TenantId, setup.Command.BranchId, 0, 50, Token)).Items); Assert.True(series.IsActive);
        var configure = source.GetRequiredService<ConfigureElectronicSeriesHandler>();
        await Assert.ThrowsAsync<ApplicationErrorException>(() => configure.HandleAsync(new SetElectronicSeriesActiveCommand(setup.Command.TenantId, setup.Command.BranchId, series.Id, false, 0), Token));
        await configure.HandleAsync(new SetElectronicSeriesActiveCommand(setup.Command.TenantId, setup.Command.BranchId, series.Id, false, series.Version), Token);
        Assert.False(Assert.Single((await query.SeriesAsync(setup.Command.TenantId, setup.Command.BranchId, 0, 50, Token)).Items).IsActive);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommercialVoidBeforeDispatchBlocksButVoidAfterReceiptNeverFakesAuthorityVoid(bool afterDispatch)
    {
        await using var services = Services(provider: new ElectronicDocumentTestData.Provider(new(DeliveryOutcome.Received, "received", "R"), false));
        var setup = await SetupAsync(services); await RequestAsync(services, setup);
        if (afterDispatch) { await using var delivery = services.CreateAsyncScope(); await delivery.ServiceProvider.GetRequiredService<RunElectronicDocumentDelivery>().HandleAsync(setup.Command.TenantId, 1, Token); }
        await using (var scope = services.CreateAsyncScope())
        {
            var source = scope.ServiceProvider; CashSessionTestData.Authenticate(source, setup.Rows.Tenant.Identity.UserId);
            var context = source.GetRequiredService<MediPosDbContext>(); context.SelectTenant(setup.Command.TenantId); var sale = await context.Sales.AsNoTracking().Where(v => v.Id == setup.Command.SaleId).Select(v => new { v.Id, Version = EF.Property<uint>(v, "Version") }).SingleAsync(Token);
            await source.GetRequiredService<MediPOS.Application.Modules.SalesPos.VoidSale.VoidSaleHandler>().HandleAsync(new(setup.Command.TenantId, setup.Command.BranchId, sale.Id, sale.Version, "Test void"), Token);
            if (!afterDispatch) await source.GetRequiredService<RunElectronicDocumentDelivery>().HandleAsync(setup.Command.TenantId, 1, Token);
            var page = await source.GetRequiredService<QueryElectronicDocumentsHandler>().HandleAsync(new(setup.Command.TenantId, setup.Command.BranchId, NeedsAttention: true), Token);
            Assert.True(Assert.Single(page.Items).CommercialVoidNeedsReview);
        }
        await using var verify = fixture.CreateContext(setup.Command.TenantId); Assert.Equal(afterDispatch ? "provider_received" : "manual_review", (await verify.ElectronicDocuments.SingleAsync(Token)).Status);
    }
    [Theory]
    [InlineData("UPDATE electronic_documents SET snapshot_json='{}'::jsonb WHERE id={0}")]
    [InlineData("UPDATE electronic_documents SET status='authority_accepted' WHERE id={0}")]
    [InlineData("DELETE FROM electronic_documents WHERE id={0}")]
    public async Task DirectSqlCannotRewriteOriginalOrInventAcceptanceOrEraseHistory(string sql)
    {
        await using var services = Services(); var setup = await SetupAsync(services); var id = await RequestAsync(services, setup);
        await using var context = fixture.CreateConstraintContext(setup.Command.TenantId);
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(sql, [id], Token)); Assert.Equal("23514", error.SqlState);
    }
    [Fact]
    public async Task DirectSqlWithoutSameTransactionAuditRollsBackAndAttemptUpdatesAreRejected()
    {
        await using var services = Services(provider: new ElectronicDocumentTestData.Provider(new(DeliveryOutcome.Received, "received", "R")));
        var setup = await SetupAsync(services); var id = await RequestAsync(services, setup);
        await using (var context = fixture.CreateConstraintContext(setup.Command.TenantId))
        {
            await using var tx = await context.Database.BeginTransactionAsync(Token);
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE electronic_documents SET status='manual_review',next_attempt_at_utc=NULL WHERE id={id}", Token);
            Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => tx.CommitAsync(Token))).SqlState);
        }
        await using (var scope = services.CreateAsyncScope()) await scope.ServiceProvider.GetRequiredService<RunElectronicDocumentDelivery>().HandleAsync(setup.Command.TenantId, 1, Token);
        await using var verify = fixture.CreateConstraintContext(setup.Command.TenantId);
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => verify.Database.ExecuteSqlInterpolatedAsync($"UPDATE electronic_document_attempts SET outcome='uncertain' WHERE document_id={id}", Token))).SqlState);
    }
    [Fact]
    public async Task CrossTenantFkAndRlsPoolingCannotLeakDocumentsOrAttemptHistory()
    {
        await using var services = Services(singleConnection: true); var setup = await SetupAsync(services); var id = await RequestAsync(services, setup);
        var (_, other) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using (var constraint = fixture.CreateConstraintContext(other.TenantId))
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => constraint.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO electronic_document_attempts(id,tenant_id,document_id,number,phase,idempotency_key,started_at_utc,outcome) VALUES ({Guid.CreateVersion7()},{other.TenantId},{id},1,'started',{"medipos-cpe-" + id.ToString("N")},{ElectronicDocumentTestData.Now},'reserved')", Token));
            Assert.Equal("23503", error.SqlState);
        }
        foreach (var tenant in new[] { setup.Command.TenantId, other.TenantId, setup.Command.TenantId })
        {
            await using var scope = services.CreateAsyncScope(); var context = scope.ServiceProvider.GetRequiredService<MediPosDbContext>(); context.SelectTenant(tenant);
            Assert.Equal(tenant == setup.Command.TenantId, await context.ElectronicDocuments.AnyAsync(v => v.Id == id, Token));
            Assert.Equal(tenant == setup.Command.TenantId ? 1 : 0, await context.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM electronic_documents").SingleAsync(Token));
        }
    }
    [Fact]
    public async Task SaleFromOtherTenantAndOtherCashierNeverReturnsOrCreatesPrivateDocument()
    {
        await using var services = Services(); var setup = await SetupAsync(services); var id = await RequestAsync(services, setup);
        var (_, other) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using (var otherScope = services.CreateAsyncScope())
        {
            var rows = await SaleCheckoutTestData.CreateAsync(otherScope.ServiceProvider, other); await SaleCheckoutTestData.ConfirmAsync(otherScope.ServiceProvider, rows);
            var foreign = setup with { Command = setup.Command with { SaleId = rows.Draft.SaleId } };
            Assert.Equal("cpe.references_not_found", (await Assert.ThrowsAsync<ApplicationErrorException>(() => RequestAsync(services, foreign))).Error.Code);
        }
        await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var user = await IdentityAccessTestSetup.CreateUserAsync(source);
        var member = await source.GetRequiredService<MediPOS.Application.Modules.IdentityAccess.CreateMembership.CreateMembershipHandler>().HandleAsync(new(setup.Command.TenantId, user.Id, MediPOS.Domain.Modules.IdentityAccess.TenantRole.Cashier, setup.Rows.Tenant.Identity.ActorId), Token);
        await source.GetRequiredService<MediPOS.Application.Modules.IdentityAccess.SetMembershipBranches.SetMembershipBranchesHandler>().HandleAsync(new(setup.Command.TenantId, member.Id, [setup.Command.BranchId], setup.Rows.Tenant.Identity.ActorId), Token);
        await source.GetRequiredService<MediPOS.Application.Modules.IdentityAccess.ReplaceWorkSchedule.ReplaceWorkScheduleHandler>().HandleAsync(new(setup.Command.TenantId, member.Id, [new(DayOfWeek.Tuesday, new(9, 0), new(18, 0))], setup.Rows.Tenant.Identity.ActorId), Token);
        CashSessionTestData.Authenticate(source, user.Id); var query = source.GetRequiredService<QueryElectronicDocumentsHandler>();
        Assert.Empty((await query.HandleAsync(new(setup.Command.TenantId, setup.Command.BranchId, Id: id), Token)).Items);
        Assert.Empty((await query.AttemptsAsync(setup.Command.TenantId, setup.Command.BranchId, id, 0, 100, Token)).Items);
        Assert.Equal("cpe.operation_denied", (await Assert.ThrowsAsync<ApplicationErrorException>(() => source.GetRequiredService<RequestElectronicDocumentHandler>().HandleAsync(setup.Command, Token))).Error.Code);
    }
    private ServiceProvider Services(ElectronicDocumentTestData.Clock? clock = null, IElectronicDocumentProvider? provider = null, bool singleConnection = false)
    {
        var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString); if (singleConnection) connection.MaxPoolSize = 1;
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:MediPosDatabase"] = connection.ConnectionString }).Build();
        var services = new ServiceCollection(); services.AddSingleton<TimeProvider>(clock ?? new ElectronicDocumentTestData.Clock()); services.AddInfrastructure(config);
        services.AddIdentityAuthentication<IdentityAccessTestSetup.TestGoogleIdentitySource, IdentityAccessTestSetup.TestServerSession>();
        services.AddSingleton<IElectronicDeliveryIdentity, TechnicalIdentity>();
        if (provider is not null) services.AddSingleton(provider);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
    private async Task<Setup> SetupAsync(ServiceProvider services)
    {
        var (tenant, _) = await TenantIsolationTestData.CreatePairAsync(fixture);
        await using var scope = services.CreateAsyncScope(); var source = scope.ServiceProvider;
        var context = source.GetRequiredService<MediPosDbContext>(); context.SelectTenant(tenant.TenantId);
        // Synthetic setup only, before the measured workflow; existing identity fixture deliberately uses RUC "123".
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE legal_entities SET ruc='20123456789' WHERE id={tenant.LegalEntityId}", Token);
        var rows = await SaleCheckoutTestData.CreateAsync(source, tenant); await SaleCheckoutTestData.ConfirmAsync(source, rows);
        var owner = await CashSessionTestData.AddOwnerAsync(source, tenant.Identity); CashSessionTestData.Authenticate(source, owner);
        var series = await source.GetRequiredService<ConfigureElectronicSeriesHandler>().HandleAsync(new ConfigureElectronicSeriesCommand(tenant.TenantId, tenant.Identity.BranchId, "receipt", "TEST1", "preparation_v1", "Address", 1), Token);
        return new(rows, await CommandAsync(source, rows, series));
    }
    private static async Task<RequestElectronicDocumentCommand> CommandAsync(IServiceProvider source, SaleCheckoutTestData.Rows rows, Guid series)
    {
        var context = source.GetRequiredService<MediPosDbContext>(); var lines = await context.SaleLines.AsNoTracking().Where(v => v.SaleId == rows.Draft.SaleId).ToArrayAsync(Token);
        return new(rows.Tenant.TenantId, rows.Tenant.Identity.BranchId, rows.Draft.SaleId, series, "receipt", new("preparation_v1", "PEN", new("dni", "12345678", "Test buyer", "Address"), lines.Select(v => new FiscalLine(v.Id, v.LineTotal, 0, "explicit_test_classification")).ToArray()));
    }
    private static async Task<Guid> RequestAsync(ServiceProvider services, Setup setup)
    {
        await using var scope = services.CreateAsyncScope(); CashSessionTestData.Authenticate(scope.ServiceProvider, setup.Rows.Tenant.Identity.UserId);
        return await scope.ServiceProvider.GetRequiredService<RequestElectronicDocumentHandler>().HandleAsync(setup.Command, Token);
    }
    private static async Task<ElectronicDeliveryClaim?> ClaimAsync(ServiceProvider services, ElectronicDeliveryIdentity identity)
    {
        await using var scope = services.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<IElectronicDeliveryWorkStore>().ClaimAsync(identity, scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow(), TimeSpan.FromMinutes(1), Token);
    }
    private async Task<string> CommercialAsync(Guid tenant)
    {
        await using var context = fixture.CreateContext(tenant);
        return JsonSerializer.Serialize(new
        {
            sales = await context.Sales.AsNoTracking().Include(v => v.Lines.OrderBy(l => l.Id)).OrderBy(v => v.Id).ToArrayAsync(Token),
            payments = await context.SalePayments.AsNoTracking().OrderBy(v => v.Id).ToArrayAsync(Token),
            cash = await context.CashSessions.AsNoTracking().OrderBy(v => v.Id).ToArrayAsync(Token),
            stock = await context.StockMovements.AsNoTracking().OrderBy(v => v.Id).ToArrayAsync(Token),
            lots = await context.InventoryLots.AsNoTracking().OrderBy(v => v.Id).ToArrayAsync(Token),
            commissions = await context.CommissionEntries.AsNoTracking().OrderBy(v => v.Id).ToArrayAsync(Token),
            products = await context.BusinessProducts.AsNoTracking().OrderBy(v => v.Id).ToArrayAsync(Token),
            units = await context.ProductUnits.AsNoTracking().OrderBy(v => v.Id).ToArrayAsync(Token)
        });
    }
    private sealed record Setup(SaleCheckoutTestData.Rows Rows, RequestElectronicDocumentCommand Command);
    private sealed class TechnicalIdentity : IElectronicDeliveryIdentity
    {
        private readonly Guid _principal = Guid.CreateVersion7();
        public Task<ElectronicDeliveryIdentity?> AuthorizeAsync(Guid tenantId, CancellationToken token) => Task.FromResult<ElectronicDeliveryIdentity?>(new(tenantId, _principal));
    }
}
