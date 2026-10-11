using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.OfflineSync;
using MediPOS.Domain.Modules.IdentityAccess;

namespace MediPOS.UnitTests.Modules.OfflineSync;

public sealed class OfflineSyncBatchTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(TenantRole.Owner)]
    [InlineData(TenantRole.Pharmacist)]
    [InlineData(TenantRole.Cashier)]
    public async Task FiftyReceiptsStayPendingWithExactOrderedAcknowledgmentsAndNoCommercialFinalization(TenantRole role)
    {
        var setup = new OfflineSyncBatchTestSetup(role); var events = Enumerable.Range(0, 50).Select(_ => setup.Envelope()).ToArray();
        var result = await setup.Execute(events);
        Assert.Equal(events.Select(e => e.EventId), result.Results.Select(r => r.EventId)); Assert.Equal(50, result.PendingCount);
        Assert.Equal(0, result.AcceptedCount + result.ConflictCount + result.RejectedCount); Assert.Equal(1, result.ProtocolVersion);
        Assert.Equal(setup.Clock.Now, result.ServerReceivedAtUtc); Assert.Null(result.NextCursor); Assert.False(result.ReceiptCursorAvailable);
        Assert.All(result.Results, receipt =>
        {
            Assert.Equal("sync.pending", receipt.ResultCode); Assert.NotNull(receipt.ReceiptId); Assert.NotNull(receipt.ReceivedAtUtc);
            Assert.Null(receipt.ProcessedAtUtc); Assert.Null(receipt.ResultReferenceId); Assert.False(receipt.IsReplay); Assert.True(receipt.Retryable);
        });
        Assert.Equal(50, setup.Data.Receipts.Count); Assert.Equal(0, setup.Data.SavedResults); Assert.Equal(0, setup.Data.ActiveScopes);
    }

    [Theory]
    [InlineData("null", "sync.batch_invalid")]
    [InlineData("null_events", "sync.batch_invalid")]
    [InlineData("empty", "sync.batch_invalid")]
    [InlineData("null_event", "sync.batch_invalid")]
    [InlineData("protocol", "sync.version_unsupported")]
    [InlineData("tenant", "sync.identity_invalid")]
    [InlineData("device", "sync.identity_invalid")]
    [InlineData("event", "sync.identity_invalid")]
    [InlineData("trace", "sync.identity_invalid")]
    [InlineData("mixed_device", "sync.batch_device_mismatch")]
    [InlineData("too_many", "sync.batch_too_many_events")]
    public async Task StructuralFailureStopsBeforeIdentityReadsOrAnyReceipt(string condition, string code)
    {
        var setup = new OfflineSyncBatchTestSetup(); var command = setup.Command(setup.Envelope());
        command = condition switch
        {
            "null" => null!,
            "null_events" => command with { Events = null },
            "empty" => command with { Events = [] },
            "null_event" => command with { Events = [null!] },
            "protocol" => command with { ProtocolVersion = 2 },
            "tenant" => command with { TenantId = Guid.Empty },
            "device" => command with { DeviceId = Guid.Empty },
            "event" => command with { Events = [setup.Envelope() with { EventId = Guid.Empty }] },
            "trace" => command with { BatchRequestId = Guid.Empty },
            "mixed_device" => command with { Events = [setup.Envelope(), setup.Envelope(device: Guid.NewGuid())] },
            _ => command with { Events = Enumerable.Range(0, 51).Select(_ => setup.Envelope()).ToArray() },
        };
        Assert.Equal(code, (await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(command, Token))).Error.Code);
        Assert.Equal(0, setup.Access.Calls); Assert.Equal(0, setup.Data.Begins); Assert.Empty(setup.Data.Receipts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WholeRequestSizeIncludesEscapingAndStopsBeforePersistence(bool escaping)
    {
        var setup = new OfflineSyncBatchTestSetup(); var envelope = setup.Envelope() with
        { Payload = new string(escaping ? '\\' : 'a', escaping ? OfflineSyncBatchPolicy.MaximumRequestBytes / 2 : OfflineSyncBatchPolicy.MaximumRequestBytes + 1) };
        Assert.Equal("sync.batch_too_large", (await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Execute(envelope))).Error.Code);
        Assert.Equal(0, setup.Access.Calls); Assert.Empty(setup.Data.Receipts);
    }

    [Fact]
    public async Task GoodBadGoodPreservesOrderAndOnlyCommitsValidPendingEvents()
    {
        var setup = new OfflineSyncBatchTestSetup(); var first = setup.Envelope(); var invalid = setup.Envelope() with { Payload = "{broken" }; var last = setup.Envelope();
        var result = await setup.Execute(first, invalid, last);
        Assert.Equal(new[] { first.EventId, invalid.EventId, last.EventId }, result.Results.Select(r => r.EventId));
        Assert.Equal(["pending", "rejected", "pending"], result.Results.Select(r => r.Status));
        Assert.Equal("sync.payload_invalid", result.Results[1].ResultCode); Assert.Null(result.Results[1].ReceiptId);
        Assert.Null(result.Results[1].ReceivedAtUtc); Assert.False(result.Results[1].Retryable);
        Assert.Equal(2, result.PendingCount); Assert.Equal(1, result.RejectedCount); Assert.Equal(2, setup.Data.Receipts.Count);
        Assert.DoesNotContain((setup.Member.TenantId, invalid.EventId), setup.Data.Receipts.Keys);
    }

    [Theory]
    [InlineData("type", "sync.type_unsupported")]
    [InlineData("version", "sync.version_unsupported")]
    [InlineData("hash", "sync.hash_mismatch")]
    [InlineData("zone", "sync.time_invalid")]
    [InlineData("time", "sync.time_invalid")]
    [InlineData("range", "sync.payload_invalid")]
    [InlineData("unicode", "sync.payload_invalid")]
    [InlineData("escaped_unicode", "sync.payload_invalid")]
    [InlineData("nul", "sync.payload_invalid")]
    [InlineData("depth", "sync.payload_invalid")]
    [InlineData("large_payload", "sync.payload_too_large")]
    [InlineData("large_event", "sync.event_too_large")]
    public async Task MalformedClientDataHasStablePerEventCodesIncludingB51EncodingErrors(string condition, string code)
    {
        var setup = new OfflineSyncBatchTestSetup(); var original = setup.Envelope();
        var invalid = condition switch
        {
            "type" => original with { EventType = "sql_command.v1" },
            "version" => original with { SchemaVersion = 2 },
            "hash" => original with { PayloadHash = new string('0', 64) },
            "zone" => original with { ClientTimeZoneId = "does/not_exist" },
            "time" => original with { ClientOccurredAtUtc = default },
            "range" => original with { Payload = "{\"x\":1e99999999999999}" },
            "unicode" => original with { Payload = "{\"x\":\"\ud800\"}" },
            "escaped_unicode" => original with { Payload = "{\"x\":\"\\ud800\"}" },
            "nul" => original with { Payload = "{\"x\":\"\\u0000\"}" },
            "depth" => original with { Payload = "{\"x\":" + new string('[', 17) + "1" + new string(']', 17) + "}" },
            "large_payload" => original with { Payload = new string('a', 65537) },
            _ => setup.Envelope(json: "{\"x\":\"" + new string('a', 65528) + "\"}")
        };
        var result = await setup.Execute(invalid, setup.Envelope()); Assert.Equal(code, result.Results[0].ResultCode);
        Assert.Equal("rejected", result.Results[0].Status); Assert.Null(result.Results[0].ReceiptId); Assert.Equal("pending", result.Results[1].Status);
        Assert.Single(setup.Data.Receipts);
    }

    [Fact]
    public async Task DuplicatesAreNotDiscardedAndIncompatibleIdentityDisclosesNoOriginalMetadata()
    {
        var setup = new OfflineSyncBatchTestSetup(); var first = setup.Envelope(); var identical = first with { Payload = " { \"quantity\":1.0 }" };
        var different = setup.Envelope(first.EventId, "{\"quantity\":2}");
        var result = await setup.Execute(first, identical, different);
        Assert.Equal(3, result.Results.Count); Assert.Equal(2, result.PendingCount); Assert.Equal(1, result.ConflictCount);
        Assert.True(result.Results[1].IsReplay); Assert.Equal(result.Results[0].ReceiptId, result.Results[1].ReceiptId);
        var conflict = result.Results[2]; Assert.Equal("sync.identity_conflict", conflict.ResultCode);
        Assert.Null(conflict.ReceiptId); Assert.Null(conflict.ResultReferenceId); Assert.Null(conflict.ReceivedAtUtc); Assert.Null(conflict.ProcessedAtUtc);
        Assert.False(conflict.IsReplay); Assert.False(conflict.Retryable); Assert.Single(setup.Data.Receipts);
        Assert.Equal(first.PayloadHash, setup.Data.Receipts.Values.Single().PayloadHash);
    }

    [Fact]
    public async Task CompleteRetryAfterLostResponseIgnoresChangedBatchIdAndRetainsReceiptTimes()
    {
        var setup = new OfflineSyncBatchTestSetup(); var events = Enumerable.Range(0, 3).Select(_ => setup.Envelope()).ToArray();
        var command = setup.Command(events) with { BatchRequestId = Guid.NewGuid() };
        var lost = await setup.Handler.HandleAsync(command, Token); setup.Clock.Now = setup.Clock.Now.AddMinutes(1);
        var retry = await setup.Handler.HandleAsync(command with { BatchRequestId = Guid.NewGuid() }, Token);
        Assert.NotEqual(lost.BatchRequestId, retry.BatchRequestId); Assert.Equal(lost.Results.Select(r => r.ReceiptId), retry.Results.Select(r => r.ReceiptId));
        Assert.Equal(lost.Results.Select(r => r.ReceivedAtUtc), retry.Results.Select(r => r.ReceivedAtUtc)); Assert.All(retry.Results, r => Assert.True(r.IsReplay));
        Assert.Equal(3, setup.Data.Receipts.Count); Assert.Equal(0, setup.Data.SavedResults);
    }

    [Fact]
    public async Task TerminalReplaysReturnSavedResultsWithoutReopeningBusinessOrLeakingPayloads()
    {
        var setup = new OfflineSyncBatchTestSetup(); var accepted = setup.Envelope(); var conflict = setup.Envelope(); var rejected = setup.Envelope();
        var saved = new[] { setup.Seed(accepted, "accepted"), setup.Seed(conflict, "conflict"), setup.Seed(rejected, "rejected") };
        var result = await setup.Execute(accepted, conflict, rejected, setup.Envelope());
        Assert.Equal(1, result.AcceptedCount); Assert.Equal(1, result.ConflictCount); Assert.Equal(1, result.RejectedCount); Assert.Equal(1, result.PendingCount);
        Assert.Equal(saved.Select(s => s.ResultCode), result.Results.Take(3).Select(r => r.ResultCode));
        Assert.Equal(saved.Select(s => s.ResultReferenceId), result.Results.Take(3).Select(r => r.ResultReferenceId));
        Assert.All(result.Results.Take(3), r => { Assert.True(r.IsReplay); Assert.False(r.Retryable); Assert.NotNull(r.ProcessedAtUtc); });
        Assert.Equal(0, setup.Data.SavedResults);
        Assert.DoesNotContain("quantity", JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("session", "access.unauthenticated")]
    [InlineData("membership", "access.membership_inactive")]
    [InlineData("license", "access.license_denied")]
    [InlineData("branch", "access.branch_denied")]
    [InlineData("assignment", "access.branch_unassigned")]
    [InlineData("schedule", "access.schedule_denied")]
    [InlineData("revoked", "sync.device_revoked")]
    public async Task CurrentAccessIsRequiredBeforeAnyEventEvenWithOldClientTimestamp(string condition, string code)
    {
        var setup = new OfflineSyncBatchTestSetup(TenantRole.Cashier); Deny(setup, condition);
        Assert.Equal(code, (await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Execute(setup.Envelope()))).Error.Code);
        Assert.Empty(setup.Data.Receipts); Assert.Equal(0, setup.Data.Reservations);
    }

    [Theory]
    [InlineData("session", "access.unauthenticated")]
    [InlineData("membership", "access.membership_inactive")]
    [InlineData("license", "access.license_denied")]
    [InlineData("revoked", "sync.device_revoked")]
    public async Task LostAccessAfterThirdCommitStopsFourthAndPreservesEarlierReceipts(string condition, string code)
    {
        var setup = new OfflineSyncBatchTestSetup(); var events = Enumerable.Range(0, 5).Select(_ => setup.Envelope()).ToArray();
        setup.Data.AfterCommit = count => { if (count == 3) Deny(setup, condition); };
        Assert.Equal(code, (await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Execute(events))).Error.Code);
        Assert.Equal(events.Take(3).Select(e => e.EventId), setup.Data.CommittedEvents); Assert.Equal(3, setup.Data.Receipts.Count);
        Assert.Equal(0, setup.Data.ActiveScopes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDoesNotFabricateResultsAndRetryRecoversCommittedPrefix(bool duringCommit)
    {
        var setup = new OfflineSyncBatchTestSetup(); var events = Enumerable.Range(0, 4).Select(_ => setup.Envelope()).ToArray();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        if (duringCommit) setup.Data.BeforeCommit = count => { if (count == 3) cancellation.Cancel(); };
        else setup.Data.AfterCommit = count => { if (count == 2) cancellation.Cancel(); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => setup.Handler.HandleAsync(setup.Command(events), cancellation.Token));
        Assert.Equal(2, setup.Data.Receipts.Count); Assert.Equal(0, setup.Data.ActiveScopes);
        setup.Data.AfterCommit = null; setup.Data.BeforeCommit = null;
        var retry = await setup.Execute(events); Assert.Equal(4, setup.Data.Receipts.Count);
        Assert.All(retry.Results.Take(2), r => Assert.True(r.IsReplay)); Assert.All(retry.Results.Skip(2), r => Assert.False(r.IsReplay));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InfrastructureFailureAbortsWithoutInventedAcknowledgmentAndReplayResolvesUncertainCommit(bool afterCommit)
    {
        var setup = new OfflineSyncBatchTestSetup(); var events = Enumerable.Range(0, 3).Select(_ => setup.Envelope()).ToArray();
        if (afterCommit) setup.Data.AfterCommit = count => { if (count == 2) throw new IOException("Connection lost after COMMIT"); };
        else setup.Data.BeforeCommit = count => { if (count == 2) throw new IOException("Connection lost before COMMIT"); };
        await Assert.ThrowsAsync<IOException>(() => setup.Execute(events)); Assert.Equal(afterCommit ? 2 : 1, setup.Data.Receipts.Count);
        setup.Data.AfterCommit = null; setup.Data.BeforeCommit = null;
        var recovered = await setup.Execute(events); Assert.Equal(3, setup.Data.Receipts.Count);
        Assert.Equal(afterCommit ? 2 : 1, recovered.Results.Count(r => r.IsReplay));
    }

    [Fact]
    public async Task CallerListMutationCannotReplaceLaterEventsAndAnotherMembershipCannotReadReceipts()
    {
        var setup = new OfflineSyncBatchTestSetup(); var first = setup.Envelope(); var second = setup.Envelope(); var events = new List<SyncEventEnvelope> { first, second };
        setup.Data.AfterCommit = count => { if (count == 1) events[1] = setup.Envelope(); };
        var result = await setup.Handler.HandleAsync(new(setup.Member.TenantId, setup.Device.Id, 1, events), Token);
        Assert.Equal(second.EventId, result.Results[1].EventId);
        setup.Data.AfterCommit = null; setup.Identity.UserId = Guid.NewGuid();
        Assert.Equal("access.tenant_mismatch", (await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Execute(first))).Error.Code);
        Assert.Equal(2, setup.Data.Receipts.Count);
    }

    private static void Deny(OfflineSyncBatchTestSetup setup, string condition)
    {
        if (condition == "session") setup.Identity.UserId = null;
        if (condition == "membership") setup.Member.Deactivate(setup.Clock.Now);
        if (condition == "license") setup.License.Suspend(setup.Member.UserId, setup.Clock.Now);
        if (condition == "branch") setup.Access.BranchBelongs = false;
        if (condition == "assignment") setup.Access.Assigned = false;
        if (condition == "schedule") setup.Clock.Now = setup.Clock.Now.AddHours(12);
        if (condition == "revoked") setup.Device.Revoke(setup.Member.Id, setup.Clock.Now);
    }

    [Fact]
    public async Task ValidOwnerCannotReceiveEventsForAnotherMembershipsDevice()
    {
        var setup = new OfflineSyncBatchTestSetup();
        var other = MediPOS.Domain.Modules.OfflineSync.SyncDevice.Register(setup.Member.TenantId, Guid.NewGuid(), setup.Device.BranchId, "Other", setup.Clock.Now);
        setup.Data.Devices.Add(other.Id, other);
        var command = new OfflineSyncBatchCommand(setup.Member.TenantId, other.Id, 1, [setup.Envelope(device: other.Id)]);
        Assert.Equal("sync.device_denied", (await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(command, Token))).Error.Code);
        Assert.Equal(0, setup.Data.Reservations); Assert.Empty(setup.Data.Receipts);
    }
}
