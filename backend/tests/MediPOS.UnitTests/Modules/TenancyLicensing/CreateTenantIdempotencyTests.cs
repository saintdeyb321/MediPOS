using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Application.Modules.TenancyLicensing.CreateTenant;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.UnitTests.Modules.TenancyLicensing;

public sealed class CreateTenantIdempotencyTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    internal static CreateTenantCommand Command(Guid actor) => new("Botica Ñandú", new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
        new(2027, 10, 1, 0, 0, 0, TimeSpan.Zero), 2, LicenseStatus.Active, actor);

    [Fact]
    public async Task LostResponseReplayReturnsOriginalWithoutRepeatingBusinessOrAudit()
    {
        var db = new TestDatabase(); var actor = Guid.NewGuid(); var key = Guid.NewGuid();
        var first = await Handle(db, Command(actor), key);
        // Caller discards the first response, then retries in a fresh application scope.
        var replay = await Handle(db, Command(actor), key);
        Assert.False(first.Replayed); Assert.True(replay.Replayed); Assert.Equal(first.License, replay.License);
        Assert.Single(db.Committed); Assert.Equal(actor, Assert.Single(db.Committed.Values).Audit.ActorId);
    }

    [Fact]
    public async Task CanonicalOffsetsTrimmingAndSubmicrosecondPrecisionMatchActualResult()
    {
        var db = new TestDatabase(); var command = Command(Guid.NewGuid()); var key = Guid.NewGuid();
        var first = await Handle(db, command with { TradingName = "  " + command.TradingName + " ", StartsAt = command.StartsAt.AddTicks(7) }, key);
        var replay = await Handle(db, command with { StartsAt = command.StartsAt.ToOffset(TimeSpan.FromHours(-5)) }, key);
        Assert.Equal(first.License, replay.License); Assert.True(replay.Replayed);
        Assert.Equal(0, first.License.StartsAt.Ticks % 10); Assert.Equal(TimeSpan.Zero, first.License.StartsAt.Offset);
        Assert.Equal("Botica Ñandú", Assert.Single(db.Committed.Values).Tenant.TradingName);
        Assert.Equal(32, Assert.Single(db.Committed.Values).Hash.Length);
        const string canonical = """["platform.tenant.create.v1","Botica \u00D1and\u00FA","2026-10-01T00:00:00.0000000\u002B00:00","2027-10-01T00:00:00.0000000\u002B00:00","2","active"]""";
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)), Assert.Single(db.Committed.Values).Hash);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("unicode")]
    [InlineData("start")]
    [InlineData("expiry")]
    [InlineData("branches")]
    [InlineData("status")]
    public async Task EveryBusinessFieldParticipatesInVersionedPayload(string field)
    {
        var db = new TestDatabase(); var command = Command(Guid.NewGuid()); var key = Guid.NewGuid();
        await Handle(db, command, key);
        var changed = field switch
        {
            "name" => command with { TradingName = "Otra" },
            "unicode" => command with { TradingName = "Botica Nandu" },
            "start" => command with { StartsAt = command.StartsAt.AddTicks(10) },
            "expiry" => command with { ExpiresAt = command.ExpiresAt.AddDays(1) },
            "branches" => command with { MaxBranches = 3 },
            _ => command with { Status = LicenseStatus.Trial }
        };
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => Handle(db, changed, key));
        Assert.Equal("platform.idempotency_conflict", error.Error.Code); Assert.Single(db.Committed);
    }

    [Fact]
    public async Task DifferentKeysAndOperatorsAreIndependent()
    {
        var db = new TestDatabase(); var command = Command(Guid.NewGuid()); var key = Guid.NewGuid();
        var a = await Handle(db, command, key); var b = await Handle(db, command, Guid.NewGuid());
        var c = await Handle(db, command with { ActorId = Guid.NewGuid() }, key);
        Assert.Equal(3, new[] { a.License.TenantId, b.License.TenantId, c.License.TenantId }.Distinct().Count());
        Assert.Equal(3, db.Committed.Count);
    }

    [Fact]
    public async Task SimulatedConcurrentEquivalentRequestsCreateOnceAtTheApplicationBoundary()
    {
        var db = new TestDatabase(); var command = Command(Guid.NewGuid()); var key = Guid.NewGuid();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Handle(db, command, key)));
        Assert.Single(results, x => !x.Replayed); Assert.Single(results.Select(x => x.License).Distinct()); Assert.Single(db.Committed);
    }

    [Theory]
    [InlineData("domain")]
    [InlineData("save")]
    [InlineData("completion")]
    public async Task FailureDoesNotPublishASuccessReceiptAndKeyCanRetry(string failure)
    {
        var db = new TestDatabase { Failure = failure }; var command = Command(Guid.NewGuid()); var key = Guid.NewGuid();
        await Assert.ThrowsAnyAsync<Exception>(() => Handle(db, failure == "domain" ? command with { MaxBranches = 0 } : command, key));
        Assert.Empty(db.Committed); db.Failure = null;
        Assert.False((await Handle(db, command, key)).Replayed); Assert.Single(db.Committed);
    }

    [Fact]
    public async Task AuthorizationIsRecheckedAfterWaitingAndBeforeCommitAndForReplays()
    {
        var db = new TestDatabase(); var actor = Guid.NewGuid(); var command = Command(actor); var key = Guid.NewGuid();
        var admin = new Authority(actor);
        db.AfterReserve = () => admin.Allowed = false;
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => Handle(db, command, key, admin));
        Assert.Equal("access.platform_denied", error.Error.Code); Assert.Empty(db.Committed);
        db.AfterReserve = null;
        await Handle(db, command, key);
        error = await Assert.ThrowsAsync<ApplicationErrorException>(() => Handle(db, command, key, admin));
        Assert.Equal("access.platform_denied", error.Error.Code); Assert.Single(db.Committed);
        var other = new Authority(actor);
        db.AfterCreate = () => other.Allowed = false;
        error = await Assert.ThrowsAsync<ApplicationErrorException>(() => Handle(db, command, Guid.NewGuid(), other));
        Assert.Equal("access.platform_denied", error.Error.Code); Assert.Single(db.Committed);
    }

    [Fact]
    public async Task ForgedActorAndInvalidKeyCannotReserve()
    {
        var db = new TestDatabase(); var command = Command(Guid.NewGuid());
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => Handle(db, command, Guid.NewGuid(),
            new Authority(Guid.NewGuid())));
        Assert.Equal("access.actor_mismatch", error.Error.Code);
        await Assert.ThrowsAsync<ApplicationErrorException>(() => Handle(db, command, Guid.Empty)); Assert.Empty(db.Committed);
    }

    internal static Task<TenantCreationResult> Handle(TestDatabase db, CreateTenantCommand command, Guid key, IPlatformAdministration? admin = null)
    {
        admin ??= new Authority(command.ActorId);
        var store = new TestStore(db);
        return new CreateTenantIdempotentlyHandler(new(store, TimeProvider.System, admin), store, admin).HandleAsync(command, key, Token);
    }

    private sealed class Authority(Guid actor) : IPlatformAdministration
    {
        internal bool Allowed = true;
        public Task<Guid?> GetAuthorizedActorAsync(CancellationToken token) => Task.FromResult<Guid?>(Allowed ? actor : null);
    }

    // Scoped test double only: exercises orchestration/HTTP, never evidence of PostgreSQL atomicity.
    internal sealed class TestDatabase
    {
        internal ConcurrentDictionary<(Guid Actor, Guid Key), (byte[] Hash, LicenseDetails Result, Tenant Tenant, AuditLog Audit)> Committed = new();
        internal ConcurrentDictionary<(Guid Actor, Guid Key), SemaphoreSlim> Gates = new();
        internal string? Failure; internal Action? AfterReserve; internal Action? AfterCreate;
    }
    internal sealed class TestStore(TestDatabase db) : ITenancyLicensingStore, ITenantCreationRequests
    {
        private Tenant? _tenant; private AuditLog? _audit;
        public Task CreateTenantAsync(Tenant tenant, AuditLog audit, CancellationToken token)
        {
            if (db.Failure == "save") throw new InvalidOperationException("Synthetic save failure.");
            _tenant = tenant; _audit = audit; db.AfterCreate?.Invoke(); return Task.CompletedTask;
        }
        public Task<License?> FindLicenseAsync(Guid tenant, Guid license, CancellationToken token) => throw new NotSupportedException();
        public Task SaveLicenseAsync(License license, AuditLog audit, CancellationToken token) => throw new NotSupportedException();
        public async Task<ITenantCreationRequest> BeginAsync(Guid actor, Guid key, byte[] hash, CancellationToken token)
        {
            var gate = db.Gates.GetOrAdd((actor, key), _ => new(1)); await gate.WaitAsync(token);
            await Task.Yield();
            try
            {
                LicenseDetails? result = null;
                if (db.Committed.TryGetValue((actor, key), out var existing))
                {
                    if (!existing.Hash.SequenceEqual(hash)) throw new ApplicationErrorException(CreateTenantIdempotentlyHandler.PayloadConflict);
                    result = existing.Result;
                }
                db.AfterReserve?.Invoke(); return new Reservation(this, gate, actor, key, hash, result, db);
            }
            catch { gate.Release(); throw; }
        }
        private sealed class Reservation(TestStore store, SemaphoreSlim gate, Guid actor, Guid key, byte[] hash,
            LicenseDetails? result, TestDatabase db) : ITenantCreationRequest
        {
            public LicenseDetails? OriginalResult => result;
            public Task CompleteAsync(LicenseDetails value, CancellationToken token)
            {
                if (db.Failure == "completion") throw new InvalidOperationException("Synthetic precommit failure.");
                Assert.NotNull(store._tenant); Assert.NotNull(store._audit);
                Assert.True(db.Committed.TryAdd((actor, key), (hash, value, store._tenant, store._audit))); return Task.CompletedTask;
            }
            public ValueTask DisposeAsync() { store._tenant = null; store._audit = null; gate.Release(); return ValueTask.CompletedTask; }
        }
    }
}
