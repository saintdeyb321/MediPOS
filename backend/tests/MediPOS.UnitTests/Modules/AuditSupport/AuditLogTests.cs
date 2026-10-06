using System.Diagnostics;
using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.SharedKernel;

namespace MediPOS.UnitTests.Modules.AuditSupport;

public sealed class AuditLogTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 9, 0, 0, TimeSpan.FromHours(-5));
    private const string Trace = "0123456789abcdef0123456789abcdef";

    [Fact]
    public void EventKeepsExactMinimalChangeAndNormalizesUtc()
    {
        const string before = """{"isActive":true}""";
        const string after = """{"isActive":false}""";
        var log = AuditLog.Create(Guid.NewGuid(), Guid.NewGuid(), AuditAction.MembershipDeactivated,
            AuditEntityType.Membership, Guid.NewGuid(), Now, Trace, before, after);
        Assert.Equal(7, log.Id.Version);
        Assert.Equal(TimeSpan.Zero, log.OccurredAt.Offset);
        Assert.Equal(Now.ToUniversalTime(), log.OccurredAt);
        using var beforeSnapshot = JsonDocument.Parse(log.BeforeJson!);
        using var afterSnapshot = JsonDocument.Parse(log.AfterJson!);
        Assert.True(beforeSnapshot.RootElement.GetProperty("isActive").GetBoolean());
        Assert.False(afterSnapshot.RootElement.GetProperty("isActive").GetBoolean());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void IdentifiersMustBePresent(int empty)
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        ids[empty] = Guid.Empty;
        Assert.Throws<ArgumentException>(() => AuditLog.Create(ids[0], ids[1], AuditAction.BranchCreated,
            AuditEntityType.Branch, ids[2], Now, Trace, null, null));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("client-free-text")]
    [InlineData("00000000000000000000000000000000")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF")]
    public void CorrelationMustBeAValidServerTrace(string correlation) =>
        Assert.Throws<ArgumentException>(() => AuditLog.Create(Guid.NewGuid(), Guid.NewGuid(), AuditAction.BranchCreated,
            AuditEntityType.Branch, Guid.NewGuid(), Now, correlation, null, null));

    [Theory]
    [InlineData((AuditAction)99, AuditEntityType.Tenant)]
    [InlineData(AuditAction.TenantCreated, (AuditEntityType)99)]
    [InlineData(AuditAction.TenantCreated, AuditEntityType.Membership)]
    public void CodesAreClosedAndEntityMustMatchAction(AuditAction action, AuditEntityType type) =>
        Assert.ThrowsAny<ArgumentException>(() => AuditLog.Create(Guid.NewGuid(), Guid.NewGuid(), action, type,
            Guid.NewGuid(), Now, Trace, null, null));

    [Theory]
    [InlineData("invalid")]
    [InlineData("[]")]
    [InlineData("\"raw\"")]
    [InlineData("null")]
    public void SnapshotsMustBeJsonObjects(string snapshot) =>
        Assert.Throws<ArgumentException>(() => AuditLog.Create(Guid.NewGuid(), Guid.NewGuid(), AuditAction.BranchCreated,
            AuditEntityType.Branch, Guid.NewGuid(), Now, Trace, snapshot, null));

    [Fact]
    public void AllSupportedActionsRoundTripThroughExplicitStableCodes()
    {
        foreach (var action in Enum.GetValues<AuditAction>())
        {
            var code = AuditCodes.ActionToCode(action);
            Assert.Equal(action, AuditCodes.ActionFromCode(code));
            Assert.Equal(AuditCodes.EntityFor(action), AuditCodes.EntityFromCode(AuditCodes.EntityToCode(AuditCodes.EntityFor(action))));
            Assert.Contains('.', code);
        }
        Assert.Throws<InvalidOperationException>(() => AuditCodes.ActionFromCode("user-provided"));
        Assert.Throws<InvalidOperationException>(() => AuditCodes.EntityFromCode("user-provided"));
    }

    [Fact]
    public void ServerActivityIsUsedAndAbsentActivityGeneratesAnIdentifier()
    {
        using var activity = new Activity("audit-test").SetIdFormat(ActivityIdFormat.W3C).Start();
        var expected = activity.TraceId.ToHexString();
        Assert.Equal(expected, ServerCorrelation.GetId());
        activity.Stop();
        var previous = Activity.Current;
        try
        {
            Activity.Current = null;
            var first = ServerCorrelation.GetId();
            var second = ServerCorrelation.GetId();
            Assert.NotEqual(first, second);
            Assert.Equal(32, first.Length);
            Assert.All(first, value => Assert.True(char.IsAsciiHexDigitLower(value)));
        }
        finally { Activity.Current = previous; }
    }

    [Fact]
    public void SnapshotsAreDeterministicAndExcludeUnrelatedEntityData()
    {
        var tenantId = Guid.NewGuid();
        var membership = Membership.Create(tenantId, Guid.NewGuid(), TenantRole.Cashier, Now);
        var early = WorkSchedule.Create(tenantId, membership.Id, tenantId, DayOfWeek.Tuesday, new(9, 0), new(12, 0));
        var late = WorkSchedule.Create(tenantId, membership.Id, tenantId, DayOfWeek.Tuesday, new(14, 0), new(18, 0));
        Assert.Equal(AuditTrail.WorkSchedule([early, late]), AuditTrail.WorkSchedule([late, early]));
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        Assert.Equal(AuditTrail.BranchAssignments([first, second]), AuditTrail.BranchAssignments([second, first]));
        using var snapshot = JsonDocument.Parse(AuditTrail.MembershipState(membership));
        Assert.Equal("cashier", snapshot.RootElement.GetProperty("role").GetString());
        Assert.False(snapshot.RootElement.TryGetProperty("email", out _));
        Assert.False(snapshot.RootElement.TryGetProperty("googleSubject", out _));
        Assert.False(snapshot.RootElement.TryGetProperty("createdAt", out _));
    }

    [Fact]
    public void MissingServerActorHasStableValidationCode()
    {
        var error = Assert.Throws<ApplicationErrorException>(() => AuditTrail.RequireActor(Guid.Empty));
        Assert.Equal("audit.actor_required", error.Error.Code);
        Assert.Equal(ErrorCategory.Validation, error.Error.Category);
    }
}
