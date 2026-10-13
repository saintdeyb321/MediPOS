using MediPOS.Application.Errors;
using MediPOS.Application.Modules.ElectronicDocuments;
using MediPOS.Domain.Modules.ElectronicDocuments;
using MediPOS.Infrastructure;
using MediPOS.TestFixtures;
using MediPOS.UnitTests.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.UnitTests.Modules.ElectronicDocuments;

[Collection("Observability")]
public sealed class ElectronicDeliveryContractTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeliveryMetricsCountCommittedDispatchAndRetainedResultWithoutCountingPolling(bool fenced)
    {
        using var metrics = new MeterProbe();
        var setup = new Setup(new(DeliveryOutcome.Received, "received", "R"), simulation: false);
        setup.Work.FenceCompletion = fenced;
        await setup.Run.HandleAsync(setup.Data.Tenant, 1, Token);
        await setup.Run.HandleAsync(setup.Data.Tenant, 1, Token);
        Assert.Single(metrics.Of("medipos.electronic_documents.dispatches"));
        Assert.Equal(fenced ? 0 : 1, metrics.Of("medipos.electronic_documents.results").Length);
        Assert.Equal(fenced ? 1 : 0, metrics.Of("medipos.electronic_documents.fenced_completions").Length);
        if (!fenced) Assert.Equal("received", Assert.Single(metrics.Of("medipos.electronic_documents.results")).Tags["outcome"]);
        Assert.Equal(2, metrics.Of("medipos.operation.executions").Length);
        Assert.All(metrics.Points.SelectMany(point => point.Tags.Keys), key => Assert.DoesNotContain("tenant", key, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task UncertainTransportIsObservedOnceAndNeverInventsTaxAcceptance()
    {
        using var metrics = new MeterProbe();
        var setup = new Setup(new(DeliveryOutcome.Received, "received", "R"), simulation: false);
        setup.Provider.Failure = new TimeoutException("sensitive transport text");
        await setup.Run.HandleAsync(setup.Data.Tenant, 1, Token);
        await setup.Run.HandleAsync(setup.Data.Tenant, 1, Token);
        Assert.Single(metrics.Of("medipos.electronic_documents.dispatches"));
        Assert.Equal("indeterminate", Assert.Single(metrics.Of("medipos.electronic_documents.results")).Tags["outcome"]);
        Assert.Equal("uncertain", setup.Document.Status);
    }

    [Fact]
    public async Task CallerCancellationAfterRetainedCompletionKeepsResultAndSeparatesCancelledRun()
    {
        using var metrics = new MeterProbe();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var setup = new Setup(new(DeliveryOutcome.Received, "received", "R"), simulation: false);
        setup.Work.CancelAfterCompletion = cancellation;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => setup.Run.HandleAsync(setup.Data.Tenant, 1, cancellation.Token));
        Assert.Equal(ElectronicDocumentStates.Received, setup.Document.Status);
        Assert.Single(metrics.Of("medipos.electronic_documents.dispatches"));
        Assert.Equal("received", Assert.Single(metrics.Of("medipos.electronic_documents.results")).Tags["outcome"]);
        Assert.Equal("cancelled", Assert.Single(metrics.Of("medipos.operation.executions")).Tags["outcome"]);
    }
    [Theory]
    [InlineData(DeliveryOutcome.Received, false, false, DeliveryOutcome.Received)]
    [InlineData(DeliveryOutcome.AuthorityAccepted, false, false, DeliveryOutcome.Indeterminate)]
    [InlineData(DeliveryOutcome.AuthorityAccepted, false, true, DeliveryOutcome.AuthorityAccepted)]
    [InlineData(DeliveryOutcome.AuthorityAccepted, true, true, DeliveryOutcome.Simulated)]
    [InlineData(DeliveryOutcome.Received, true, false, DeliveryOutcome.Simulated)]
    [InlineData(DeliveryOutcome.Rejected, true, false, DeliveryOutcome.Simulated)]
    [InlineData(DeliveryOutcome.Rejected, false, false, DeliveryOutcome.Rejected)]
    [InlineData(DeliveryOutcome.TemporaryNotReceived, false, false, DeliveryOutcome.TemporaryNotReceived)]
    [InlineData(DeliveryOutcome.Indeterminate, false, false, DeliveryOutcome.Indeterminate)]
    [InlineData(DeliveryOutcome.Simulated, false, false, DeliveryOutcome.Indeterminate)]
    public void HttpSuccessSimulationAndExternalEvidenceAreDistinct(DeliveryOutcome outcome, bool simulation, bool verified, DeliveryOutcome expected)
    {
        var result = new ElectronicProviderResult(outcome, "ok", "R1", new("SUNAT", new string('a', 64), "verification_reference"));
        Assert.Equal(expected, ElectronicProviderContract.Normalize(result, simulation, verified).Outcome);
    }
    [Theory]
    [InlineData(DeliveryOutcome.Received, "received")]
    [InlineData(DeliveryOutcome.AuthorityAccepted, "accepted")]
    [InlineData(DeliveryOutcome.Rejected, "rejected")]
    [InlineData(DeliveryOutcome.TemporaryNotReceived, "temporary_not_received")]
    [InlineData(DeliveryOutcome.Indeterminate, "indeterminate")]
    public async Task EverySimulatedOutcomeRemainsDiagnosticTestEvidenceThroughoutDelivery(DeliveryOutcome outcome, string label)
    {
        var source = new ElectronicProviderResult(outcome, "TEST_CODE_42", "TEST_REF", new("SUNAT", new string('a', 64), "synthetic_evidence"));
        var normalized = ElectronicProviderContract.Normalize(source, simulation: true, verifiedEvidence: true);
        var diagnostic = "simulation." + label + ".TEST_CODE_42";
        Assert.Equal(DeliveryOutcome.Simulated, normalized.Outcome); Assert.Equal(diagnostic, normalized.Code);
        Assert.Equal("TEST_REF", normalized.Reference); Assert.Null(normalized.Evidence);
        var setup = new Setup(source);
        Assert.Equal(1, await setup.Run.HandleAsync(setup.Data.Tenant, 1, Token));
        Assert.Equal("simulated", setup.Document.Status); Assert.Equal("simulation", setup.Document.ProviderStatus);
        Assert.Equal(diagnostic, setup.Document.LastErrorCode); Assert.Null(setup.Document.EvidenceSha256); Assert.Null(setup.Document.NextAttemptAtUtc);
        var finished = Assert.Single(setup.Work.Events, v => v.Phase == "completed");
        Assert.Equal("simulated", finished.Outcome); Assert.Equal(diagnostic, finished.ErrorCode); Assert.Equal("TEST_REF", finished.ProviderReference);
        setup.Clock.NowUtc = setup.Clock.NowUtc.AddDays(1);
        Assert.Equal(0, await setup.Run.HandleAsync(setup.Data.Tenant, 1, Token)); Assert.Equal(1, setup.Provider.Calls);
    }
    [Fact]
    public void SimulatedRejectionDiagnosticsRespectPersistentLimitWithoutCopyingEvidence()
    {
        var result = ElectronicProviderContract.Normalize(new(DeliveryOutcome.Rejected, new string('E', 64), "TEST_REF", new("SUNAT", new string('a', 64), "synthetic")), true, true);
        Assert.Equal(DeliveryOutcome.Simulated, result.Outcome); Assert.Equal(64, result.Code.Length);
        Assert.StartsWith("simulation.rejected.", result.Code, StringComparison.Ordinal); Assert.EndsWith(new string('E', 44), result.Code, StringComparison.Ordinal);
        Assert.Null(result.Evidence);
    }
    [Theory]
    [InlineData("token=secret")]
    [InlineData("bad\nresponse")]
    public void InvalidSimulatedMetadataIsSanitizedAndStillCannotClaimRealOutcome(string unsafeText)
    {
        var result = ElectronicProviderContract.Normalize(new(DeliveryOutcome.Rejected, unsafeText, unsafeText), true, true);
        Assert.Equal(DeliveryOutcome.Simulated, result.Outcome); Assert.Equal("simulation.indeterminate.provider_metadata_invalid", result.Code);
        Assert.Null(result.Reference); Assert.Null(result.Evidence); Assert.DoesNotContain(unsafeText, result.Code, StringComparison.Ordinal);
    }
    [Fact]
    public async Task SimulatedTransportTimeoutIsLabelledAsSimulationRatherThanTaxRejection()
    {
        var setup = new Setup(new(DeliveryOutcome.Rejected, "TEST_REJECTION")); setup.Provider.Failure = new TimeoutException("sensitive transport detail");
        await setup.Run.HandleAsync(setup.Data.Tenant, 1, Token);
        Assert.Equal("simulated", setup.Document.Status); Assert.Equal("simulation.indeterminate.delivery_transport_unknown", setup.Document.LastErrorCode);
        Assert.Null(setup.Document.EvidenceSha256); Assert.Null(setup.Document.NextAttemptAtUtc);
        Assert.Equal(setup.Document.LastErrorCode, Assert.Single(setup.Work.Events, v => v.Phase == "completed").ErrorCode);
    }
    [Theory]
    [InlineData("token=secret")]
    [InlineData("bad\nresponse")]
    [InlineData("")]
    [InlineData("https://response")]
    public void UntrustedProviderMetadataIsDiscardedWithoutLeakingRawResponse(string unsafeText)
    {
        var result = ElectronicProviderContract.Normalize(new(DeliveryOutcome.Received, unsafeText, unsafeText), false, false);
        Assert.Equal(DeliveryOutcome.Indeterminate, result.Outcome); Assert.Equal("provider_metadata_invalid", result.Code); Assert.Null(result.Reference); Assert.Null(result.Evidence);
    }
    [Fact]
    public async Task NormalDiHasNoSimulatedProviderOrHostedDeliveryAndRequiresTechnicalIdentity()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:MediPosDatabase"] = "Host=localhost;Database=unused" }).Build();
        var services = new ServiceCollection(); services.AddInfrastructure(config);
        await using var source = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        Assert.False(source.GetRequiredService<IElectronicDocumentProvider>().Capabilities.Operational); Assert.False(source.GetRequiredService<IElectronicDocumentProvider>().Capabilities.Simulation);
        Assert.Null(await source.GetRequiredService<IElectronicDeliveryIdentity>().AuthorizeAsync(Guid.CreateVersion7(), Token));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(ConfigureElectronicSeriesHandler));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(RequestElectronicDocumentHandler));
        await using var scope = source.CreateAsyncScope();
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => scope.ServiceProvider.GetRequiredService<RunElectronicDocumentDelivery>().HandleAsync(Guid.CreateVersion7(), 1, Token));
        Assert.Equal("cpe.technical_identity_denied", error.Error.Code);
    }
    [Fact]
    public async Task ExplicitSimulatedEndToEndRunRecordsHistoryButNeverAuthorityAcceptance()
    {
        var setup = new Setup(new(DeliveryOutcome.AuthorityAccepted, "http200", "R1", new("SUNAT", new string('a', 64), "fake")));
        Assert.Equal(1, await setup.Run.HandleAsync(setup.Data.Tenant, 10, Token));
        Assert.Equal("simulated", setup.Document.Status); Assert.Null(setup.Document.EvidenceSha256);
        Assert.Equal(2, setup.Work.Events.Count); Assert.Equal(1, setup.Provider.Calls);
        Assert.Equal(0, await setup.Run.HandleAsync(setup.Data.Tenant, 10, Token)); Assert.Equal(1, setup.Provider.Calls);
    }
    [Theory]
    [InlineData("timeout")]
    [InlineData("network")]
    [InlineData("io")]
    [InlineData("cancelled")]
    public async Task TransportFailuresAfterDispatchBecomeUncertainAndAreNeverResent(string failure)
    {
        var setup = new Setup(new(DeliveryOutcome.Received, "received", "R"), simulation: false);
        setup.Provider.Failure = failure switch { "timeout" => new TimeoutException("sensitive"), "network" => new HttpRequestException("sensitive"), "io" => new IOException("sensitive"), _ => new OperationCanceledException() };
        await setup.Run.HandleAsync(setup.Data.Tenant, 1, Token); Assert.Equal("uncertain", setup.Document.Status);
        setup.Clock.NowUtc = setup.Clock.NowUtc.AddDays(1); Assert.Equal(0, await setup.Run.HandleAsync(setup.Data.Tenant, 1, Token));
        Assert.Equal(1, setup.Provider.Calls); Assert.Equal("delivery_transport_unknown", setup.Document.LastErrorCode);
    }
    [Fact]
    public async Task TemporaryUnequivocallyNotReceivedHonorsPersistedBackoffAndMaximum()
    {
        var setup = new Setup(new(DeliveryOutcome.TemporaryNotReceived, "not_received"), simulation: false);
        for (var i = 1; i <= 5; i++)
        {
            Assert.Equal(1, await setup.Run.HandleAsync(setup.Data.Tenant, 100, Token)); Assert.Equal(i, setup.Provider.Calls);
            Assert.Equal(0, await setup.Run.HandleAsync(setup.Data.Tenant, 100, Token));
            if (i < 5) setup.Clock.NowUtc = setup.Document.NextAttemptAtUtc!.Value;
        }
        Assert.Equal("manual_review", setup.Document.Status); Assert.Equal(10, setup.Work.Events.Count);
    }
    [Fact]
    public async Task ExpiredResponseCannotOverwriteStateAndRestartRecoversAsUncertain()
    {
        var setup = new Setup(new(DeliveryOutcome.Received, "received", "R"));
        var claim = await setup.Work.ClaimAsync(setup.Identity.Principal, setup.Clock.NowUtc, TimeSpan.FromMinutes(1), Token);
        await setup.Work.BeginDispatchAsync(setup.Identity.Principal, claim!, setup.Clock.NowUtc, Token);
        setup.Clock.NowUtc = setup.Clock.NowUtc.AddMinutes(1);
        Assert.False(await setup.Work.CompleteAsync(setup.Identity.Principal, claim!, new(DeliveryOutcome.Received, "late", "R"), setup.Clock.NowUtc, Token));
        await setup.Run.HandleAsync(setup.Data.Tenant, 1, Token); Assert.Equal("uncertain", setup.Document.Status); Assert.Equal(0, setup.Provider.Calls);
    }
    [Fact]
    public async Task ProgrammerDefectIsNotDisguisedAsSuccessAndDispatchIntentSurvivesForRecovery()
    {
        var setup = new Setup(new(DeliveryOutcome.Received, "received", "R")); setup.Provider.Failure = new InvalidOperationException("bug");
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Run.HandleAsync(setup.Data.Tenant, 1, Token));
        Assert.Equal("dispatching", setup.Document.Status); setup.Clock.NowUtc = setup.Clock.NowUtc.AddMinutes(1);
        await setup.Run.HandleAsync(setup.Data.Tenant, 1, Token); Assert.Equal("uncertain", setup.Document.Status); Assert.Equal(1, setup.Provider.Calls);
    }
    [Fact]
    public async Task VoidBeforeDispatchNeverCallsProviderAndLeavesManualEvidence()
    {
        var setup = new Setup(new(DeliveryOutcome.Received, "received", "R")); setup.Work.SaleConfirmed = false;
        await setup.Run.HandleAsync(setup.Data.Tenant, 1, Token); Assert.Equal("manual_review", setup.Document.Status);
        Assert.Equal(0, setup.Provider.Calls); Assert.Equal(2, setup.Work.Events.Count);
    }
    private sealed class Setup
    {
        internal ElectronicDocumentTestData Data { get; } = new();
        internal ElectronicDocument Document { get; }
        internal ElectronicDocumentTestData.Clock Clock { get; } = new();
        internal ElectronicDocumentTestData.Provider Provider { get; }
        internal ElectronicDocumentTestData.Identity Identity { get; }
        internal Work Work { get; }
        internal RunElectronicDocumentDelivery Run { get; }
        internal Setup(ElectronicProviderResult result, bool simulation = true)
        {
            Document = Data.Create(); Provider = new(result, simulation); Identity = new(Data.Tenant); Work = new(Document);
            Run = new(Work, Provider, Identity, new ElectronicDocumentTestData.EvidenceVerifier(), Clock);
        }
    }
    // Pure domain/port contract double, never used to assert PostgreSQL persistence or concurrency.
    private sealed class Work(ElectronicDocument document) : IElectronicDeliveryWorkStore
    {
        internal List<ElectronicDocumentAttempt> Events { get; } = [];
        internal bool SaleConfirmed { get; set; } = true;
        internal bool FenceCompletion { get; set; }
        internal CancellationTokenSource? CancelAfterCompletion { get; set; }
        public Task<int> RecoverExpiredAsync(ElectronicDeliveryIdentity identity, DateTimeOffset now, int limit, CancellationToken token)
        {
            if (document.Status is ElectronicDocumentStates.Reserved or ElectronicDocumentStates.Dispatching && document.LeaseExpiresAtUtc <= now)
            { document.RecoverExpired(now); Events.Add(ElectronicDocumentAttempt.Record(document, true, now)); return Task.FromResult(1); }
            return Task.FromResult(0);
        }
        public Task<ElectronicDeliveryClaim?> ClaimAsync(ElectronicDeliveryIdentity identity, DateTimeOffset now, TimeSpan lease, CancellationToken token)
        {
            if (document.Status is not (ElectronicDocumentStates.Pending or ElectronicDocumentStates.Retry) || document.NextAttemptAtUtc > now) return Task.FromResult<ElectronicDeliveryClaim?>(null);
            var claim = document.Claim(now, lease); Events.Add(ElectronicDocumentAttempt.Record(document, false, now));
            return Task.FromResult<ElectronicDeliveryClaim?>(new(document.TenantId, document.Id, claim, document.IdempotencyKey, document.SnapshotJson, document.DocumentNumber));
        }
        public Task<bool> BeginDispatchAsync(ElectronicDeliveryIdentity identity, ElectronicDeliveryClaim claim, DateTimeOffset now, CancellationToken token)
        {
            document.BeginDispatch(claim.Token, SaleConfirmed, now);
            if (!SaleConfirmed) Events.Add(ElectronicDocumentAttempt.Record(document, true, now)); return Task.FromResult(SaleConfirmed);
        }
        public Task<bool> CompleteAsync(ElectronicDeliveryIdentity identity, ElectronicDeliveryClaim claim, ElectronicProviderResult result, DateTimeOffset now, CancellationToken token)
        {
            if (FenceCompletion) return Task.FromResult(false);
            if (claim.Token != document.ClaimToken || now >= document.LeaseExpiresAtUtc) return Task.FromResult(false);
            document.Complete(claim.Token, result.Outcome, result.Reference, result.Code, result.Evidence?.EvidenceSha256, now);
            CancelAfterCompletion?.Cancel();
            Events.Add(ElectronicDocumentAttempt.Record(document, true, now)); return Task.FromResult(true);
        }
    }
}
