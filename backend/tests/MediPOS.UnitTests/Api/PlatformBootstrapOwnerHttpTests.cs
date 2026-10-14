using System.Net;
using System.Text;
using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.CreateMembership;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.TenancyLicensing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using GoogleHost = MediPOS.UnitTests.Api.GoogleAuthenticationHttpTests.Host;

namespace MediPOS.UnitTests.Api;

// Real HTTPS/OIDC/session/CSRF/platform admission and application handler. Persistence is
// scoped synthetic staging, not evidence for PostgreSQL locking, rollback or RLS.
[Collection("Observability")]
public sealed class PlatformBootstrapOwnerHttpTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PriorGoogleRegistrationOwnMeAndVerifiedUuidProvisionOnlyTargetWithServerActor()
    {
        var state = new State(); state.Users.Remove(state.Target.Id);
        await using var host = await Host(state);
        host.Provider.Subject = state.Target.GoogleSubject; await PlatformLicenseHttpTests.Login(host);
        using var me = JsonDocument.Parse(await host.Client.GetStringAsync("/auth/me", Token));
        state.Target = state.Users[me.RootElement.GetProperty("userId").GetGuid()];
        Assert.NotEqual(state.Actor.Id, state.Target.Id);
        host.Provider.Subject = state.Actor.GoogleSubject; await PlatformLicenseHttpTests.Login(host);
        var csrf = await host.Csrf();
        using var response = await Send(host, state, csrf, suffix: "?ActorId=" + state.Target.Id + "&role=Cashier&userId=" + state.Actor.Id);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); Assert.Null(response.Headers.Location); Assert.False(response.Headers.Contains("Idempotency-Replayed"));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)); var dto = json.RootElement;
        Assert.Equal(6, dto.EnumerateObject().Count()); Assert.Equal(state.License.TenantId, dto.GetProperty("tenantId").GetGuid());
        Assert.Equal(state.Target.Id, dto.GetProperty("userId").GetGuid()); Assert.Equal("owner", dto.GetProperty("role").GetString()); Assert.True(dto.GetProperty("isActive").GetBoolean());
        Assert.Equal(TimeSpan.Zero, dto.GetProperty("createdAt").GetDateTimeOffset().Offset);
        var member = Assert.Single(state.Members); var audit = Assert.Single(state.Audits);
        Assert.Equal(member.Id, dto.GetProperty("membershipId").GetGuid()); Assert.Equal(state.Actor.Id, audit.ActorId);
        Assert.Equal(AuditAction.MembershipCreated, audit.Action); Assert.Equal(member.Id, audit.EntityId); Assert.Equal(TenantRole.Owner, member.Role);
        // Independent platform approval conveys no tenant membership to the operator.
        using var tenantDenied = await host.Client.GetAsync("/__tenant-check", Token); await Problem(tenantDenied, HttpStatusCode.Forbidden, "access.membership_missing");
        host.Provider.Subject = state.Target.GoogleSubject; await PlatformLicenseHttpTests.Login(host);
        Assert.Equal("allowed", await host.Client.GetStringAsync("/__tenant-check", Token));
        using var platformDenied = await Send(host, state, await host.Csrf()); await Problem(platformDenied, HttpStatusCode.Forbidden, "access.platform_denied");
        Assert.Single(state.Members); Assert.Single(state.Audits);
    }

    [Fact]
    public async Task AnonymousAndRealTenantOwnerCannotReachBootstrapPersistence()
    {
        var state = new State(); state.Members.Add(Membership.Create(state.License.TenantId, state.Target.Id, TenantRole.Owner, DateTimeOffset.UtcNow));
        await using var host = await Host(state);
        using var anonymous = await Send(host, state, body: "malformed"); await Problem(anonymous, HttpStatusCode.Unauthorized, "auth.session_required");
        host.Provider.Subject = state.Target.GoogleSubject; await PlatformLicenseHttpTests.Login(host);
        Assert.Equal("allowed", await host.Client.GetStringAsync("/__tenant-check", Token));
        using var denied = await Send(host, state, await host.Csrf(), body: "malformed"); await Problem(denied, HttpStatusCode.Forbidden, "access.platform_denied");
        Assert.Equal(0, state.BeginCalls); Assert.Empty(state.Audits);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("invalid")]
    [InlineData("zero")]
    [InlineData("compact")]
    [InlineData("braced")]
    [InlineData("padded")]
    public async Task UserIdentifierMustBeNonemptyExactUuid(string defect)
    {
        var state = new State(); await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host);
        using var response = await Send(host, state, await host.Csrf(), JsonSerializer.Serialize(new { userId = Identifier(state.Target.Id, defect) }));
        await Problem(response, HttpStatusCode.BadRequest, "platform.invalid_identifier"); Assert.Equal(0, state.BeginCalls); Assert.Empty(state.Members);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("zero")]
    [InlineData("compact")]
    [InlineData("braced")]
    [InlineData("padded")]
    public async Task TenantIdentifierMustBeNonemptyExactUuid(string defect)
    {
        var state = new State(); await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host);
        using var response = await Send(host, state, await host.Csrf(), tenant: Identifier(state.License.TenantId, defect));
        await Problem(response, HttpStatusCode.BadRequest, "platform.invalid_identifier"); Assert.Equal(0, state.BeginCalls); Assert.Empty(state.Members);
    }

    [Fact]
    public async Task MissingTenantRouteSegmentDoesNotMapOrWrite()
    {
        var state = new State(); await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host);
        using var response = await Send(host, state, await host.Csrf(), tenant: "");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, state.BeginCalls); Assert.Empty(state.Members); Assert.Empty(state.Audits);
    }

    [Theory]
    [InlineData("actorId")]
    [InlineData("role")]
    [InlineData("isSuperadmin")]
    [InlineData("tenantId")]
    [InlineData("googleSubject")]
    [InlineData("email")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("number")]
    [InlineData("array")]
    [InlineData("malformed")]
    public async Task OnlyOneRequiredUserIdIsAccepted(string defect)
    {
        var state = new State(); await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host);
        var valid = JsonSerializer.Serialize(new { userId = state.Target.Id.ToString("D") });
        var body = defect switch
        {
            "duplicate" => valid[..^1] + ",\"userId\":\"" + state.Actor.Id + "\"}",
            "missing" => "{}",
            "null" => "{\"userId\":null}",
            "number" => "{\"userId\":1}",
            "array" => "[]",
            "malformed" => "{",
            _ => valid[..^1] + ",\"" + defect + "\":\"synthetic_private_payload\"}"
        };
        using var response = await Send(host, state, await host.Csrf(), body);
        await Problem(response, HttpStatusCode.BadRequest, defect == "null" ? "platform.invalid_identifier" : "platform.invalid_json");
        Assert.Equal(0, state.BeginCalls); Assert.Empty(state.Members); Assert.Empty(state.Audits);
    }

    [Theory]
    [InlineData("user", "user.not_found")]
    [InlineData("tenant", "license.not_found")]
    [InlineData("expired", "license.operation_denied")]
    [InlineData("future", "license.operation_denied")]
    [InlineData("suspended", "license.operation_denied")]
    [InlineData("cancelled", "license.operation_denied")]
    [InlineData("purge_pending", "license.operation_denied")]
    [InlineData("purged", "license.operation_denied")]
    public async Task ExistingHandlerPreservesTargetAndLicenseDenials(string defect, string code)
    {
        var state = new State(); var now = DateTimeOffset.UtcNow;
        if (defect == "user") state.Users.Remove(state.Target.Id);
        if (defect == "tenant") state.MissingTenant = true;
        var status = defect switch { "suspended" => LicenseStatus.Suspended, "cancelled" => LicenseStatus.Cancelled, "purge_pending" => LicenseStatus.PurgePending, "purged" => LicenseStatus.Purged, _ => LicenseStatus.Active };
        state.License = License.Create(state.License.TenantId, defect == "future" ? now.AddDays(1) : now.AddDays(-2),
            defect == "expired" ? now.AddDays(-1) : now.AddDays(30), 3, status, state.Actor.Id, now);
        await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host);
        using var response = await Send(host, state, await host.Csrf());
        await Problem(response, defect is "user" or "tenant" ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden, code);
        Assert.Empty(state.Members); Assert.Empty(state.Audits); Assert.Equal(0, state.Commits);
    }

    [Fact]
    public async Task LostResponseRetryAndInactiveHistoricalOwnerNeverReopenBootstrap()
    {
        var state = new State(); await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host); var csrf = await host.Csrf();
        using var first = await Send(host, state, csrf); Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var retry = await Send(host, state, csrf); await Problem(retry, HttpStatusCode.Forbidden, "access.bootstrap_denied");
        Assert.False(retry.Headers.Contains("Idempotency-Replayed")); Assert.Single(state.Members); Assert.Single(state.Audits);
        state.Members[0].Deactivate(DateTimeOffset.UtcNow);
        using var historical = await Send(host, state, csrf); await Problem(historical, HttpStatusCode.Forbidden, "access.bootstrap_denied");
        Assert.Single(state.Members); Assert.False(state.Members[0].IsActive); Assert.Single(state.Audits); Assert.Equal(1, state.Commits);
    }

    [Theory]
    [InlineData("csrf-missing")]
    [InlineData("csrf-invalid")]
    [InlineData("origin-missing")]
    [InlineData("origin-forged")]
    [InlineData("host-forged")]
    public async Task ExistingOriginHostAndCsrfBoundaryRunsBeforePersistence(string defect)
    {
        var state = new State(); await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host);
        using var request = Request(state, defect == "csrf-missing" ? null : defect == "csrf-invalid" ? "invalid" : await host.Csrf());
        if (defect.StartsWith("origin", StringComparison.Ordinal)) { request.Headers.Remove("Origin"); if (defect == "origin-forged") request.Headers.Add("Origin", "https://foreign.test"); }
        if (defect == "host-forged") request.Headers.Host = "foreign.test";
        using var response = await host.Client.SendAsync(request, Token);
        await Problem(response, defect.StartsWith("csrf", StringComparison.Ordinal) ? HttpStatusCode.BadRequest : HttpStatusCode.Forbidden,
            defect.StartsWith("csrf", StringComparison.Ordinal) ? "auth.csrf_invalid" : "auth.origin_denied");
        Assert.Equal(0, state.BeginCalls); Assert.Empty(state.Audits);
    }

    [Theory]
    [InlineData("size")]
    [InlineData("media")]
    [InlineData("compression")]
    public async Task SharedJsonBodyLimitsAreEnforced(string defect)
    {
        var state = new State(); await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host);
        using var request = Request(state, await host.Csrf(), defect == "size" ? new string('x', 4097) : null);
        if (defect == "media") request.Content!.Headers.ContentType = new("text/plain");
        if (defect == "compression") request.Content!.Headers.ContentEncoding.Add("gzip");
        using var response = await host.Client.SendAsync(request, Token);
        await Problem(response, defect == "size" ? HttpStatusCode.RequestEntityTooLarge : HttpStatusCode.UnsupportedMediaType,
            defect == "size" ? "platform.body_too_large" : "platform.json_required"); Assert.Equal(0, state.BeginCalls);
    }

    [Fact]
    public async Task QuotaIsSharedWithLicenseAdministrationAndRejectsBeforePersistence()
    {
        var state = new State(); await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host); var csrf = await host.Csrf();
        for (var index = 0; index < 10; index++) { using var invalid = await Send(host, state, csrf, "{}"); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode); }
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/platform/tenants/{state.License.TenantId:D}/licenses/{state.License.Id:D}/suspend") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        request.Headers.Add("Origin", "https://medipos.example.test"); request.Headers.Add("X-CSRF-TOKEN", csrf);
        using var limited = await host.Client.SendAsync(request, Token); await Problem(limited, HttpStatusCode.TooManyRequests, "platform.rate_limited"); Assert.Equal(0, state.BeginCalls);
    }

    [Fact]
    public async Task OverlapUsesExistingAdmissionAndLeaseIsReleasedAfterSuccess()
    {
        var state = new State { Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host); var csrf = await host.Csrf(); var firstTask = Send(host, state, csrf);
        try
        {
            await state.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
            using var overlap = await Send(host, state, csrf); await Problem(overlap, HttpStatusCode.TooManyRequests, "platform.rate_limited");
        }
        finally { state.Release.TrySetResult(); }
        using var first = await firstTask; Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var retry = await Send(host, state, csrf); await Problem(retry, HttpStatusCode.Forbidden, "access.bootstrap_denied"); Assert.Single(state.Audits);
    }

    [Theory]
    [InlineData("persistence")]
    [InlineData("expiration")]
    [InlineData("revocation")]
    public async Task BootstrapRechecksBeforeCommitAndDiscardsStagedWrites(string defect)
    {
        var state = new State(); var authority = new PlatformAdministrationStub(state.Actor.Id); var store = new Store(state);
        state.License = License.Create(state.License.TenantId, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddMinutes(5), 3, LicenseStatus.Active, state.Actor.Id, DateTimeOffset.UtcNow);
        state.AfterWrite = () =>
        {
            if (defect == "persistence") throw new InvalidOperationException("Synthetic persistence failure.");
            if (defect == "expiration") state.Clock.Offset = TimeSpan.FromMinutes(10);
            if (defect == "revocation") authority.Actor = null;
        };
        var handler = new CreateMembershipHandler(store, store, state.Clock, new BusinessRuleAuthorizationStub(), authority);
        if (defect == "persistence") await Assert.ThrowsAsync<InvalidOperationException>(() => handler.BootstrapOwnerAsync(state.License.TenantId, state.Target.Id, state.Actor.Id, Token));
        else
        {
            var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => handler.BootstrapOwnerAsync(state.License.TenantId, state.Target.Id, state.Actor.Id, Token));
            Assert.Equal(defect == "expiration" ? "license.operation_denied" : "access.platform_denied", error.Error.Code);
        }
        Assert.Empty(state.Members); Assert.Empty(state.Audits); Assert.Equal(0, state.Commits);
    }

    [Fact]
    public async Task RequiredBootstrapCannotFallBackToOrdinaryOwnerAdministration()
    {
        var state = new State(); var store = new Store(state);
        var handler = new CreateMembershipHandler(store, store, state.Clock, new BusinessRuleAuthorizationStub(), new PlatformAdministrationStub(null));
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => handler.BootstrapOwnerAsync(state.License.TenantId, state.Target.Id, state.Actor.Id, Token));
        Assert.Equal("access.platform_denied", error.Error.Code); Assert.Equal(0, state.BeginCalls); Assert.Empty(state.Audits);
    }

    [Fact]
    public async Task SanitizedProblemsAndLogsNeverExposeTargetIdentityOrPayload()
    {
        var previous = Console.Out; using var captured = new StringWriter(); Console.SetOut(TextWriter.Synchronized(captured)); var state = new State();
        try
        {
            await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host);
            using var denied = await Send(host, state, await host.Csrf(), "{\"userId\":\"synthetic_private_payload\"}");
            await Problem(denied, HttpStatusCode.BadRequest, "platform.invalid_identifier");
        }
        finally { Console.SetOut(previous); }
        Assert.False(string.IsNullOrWhiteSpace(captured.ToString()));
        foreach (var secret in new[] { state.Actor.Id.ToString("D"), state.Target.Id.ToString("D"), state.Actor.GoogleSubject, state.Target.GoogleSubject, "synthetic_private_payload" })
            Assert.DoesNotContain(secret, captured.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static Task<GoogleHost> Host(State state) => GoogleHost.CreateAsync(testRoutes: app => app.MapGet("/__tenant-check", async (ITenantOperationAuthorizer authorizer) =>
    { await authorizer.RequireAsync(state.License.TenantId, null, null, TenantOperation.Administration, Token); return "allowed"; }),
        configure: config => { config["PlatformApi:Enabled"] = "true"; config["PlatformAdministration:Enabled"] = "true"; config["PlatformAdministration:AdministratorUserIds:0"] = state.Actor.Id.ToString("D"); },
        configureServices: services =>
        {
            services.AddSingleton<TimeProvider>(state.Clock); services.AddScoped(_ => new Store(state));
            services.AddScoped<IIdentityAccessStore>(provider => provider.GetRequiredService<Store>());
            services.AddScoped<ITenantLicenseProvisioning>(provider => provider.GetRequiredService<Store>());
            services.AddScoped<IOperationalAccessReader>(_ => new Reader(state));
        });
    private static string Identifier(Guid id, string defect) => defect switch
    { "empty" => "", "zero" => Guid.Empty.ToString("D"), "compact" => id.ToString("N"), "braced" => id.ToString("B"), "padded" => " " + id.ToString("D") + " ", _ => "invalid" };
    private static HttpRequestMessage Request(State state, string? csrf, string? body = null, string? tenant = null, string suffix = "")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/platform/tenants/{Uri.EscapeDataString(tenant ?? state.License.TenantId.ToString("D"))}/bootstrap-owner{suffix}")
        { Content = new StringContent(body ?? JsonSerializer.Serialize(new { userId = state.Target.Id.ToString("D") }), Encoding.UTF8, "application/json") };
        request.Headers.Add("Origin", "https://medipos.example.test"); request.Headers.Add("ActorId", Guid.NewGuid().ToString("D"));
        if (csrf is not null) request.Headers.Add("X-CSRF-TOKEN", csrf); return request;
    }
    private static async Task<HttpResponseMessage> Send(GoogleHost host, State state, string? csrf = null, string? body = null, string? tenant = null, string suffix = "")
    { using var request = Request(state, csrf, body, tenant, suffix); return await host.Client.SendAsync(request, Token); }
    private static async Task Problem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode); Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)); Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        Assert.True(json.RootElement.TryGetProperty("traceId", out _)); Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.DoesNotContain("synthetic_private", json.RootElement.ToString(), StringComparison.Ordinal);
    }

    private sealed class Clock : TimeProvider { internal TimeSpan Offset; public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + Offset; }
    private sealed class State
    {
        internal readonly User Actor = PlatformLicenseHttpTests.Operator();
        internal User Target = User.Create("synthetic-owner-subject", "same@example.test", "Synthetic owner", DateTimeOffset.UtcNow);
        internal readonly Dictionary<Guid, User> Users = []; internal readonly List<Membership> Members = []; internal readonly List<AuditLog> Audits = [];
        internal readonly Clock Clock = new(); internal License License; internal bool MissingTenant; internal int BeginCalls; internal int Commits;
        internal Action? AfterWrite; internal TaskCompletionSource? Entered; internal TaskCompletionSource? Release;
        internal State()
        {
            Users.Add(Actor.Id, Actor); Users.Add(Target.Id, Target);
            License = License.Create(Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30), 3, LicenseStatus.Active, Actor.Id, DateTimeOffset.UtcNow);
        }
    }
    private sealed class Store(State state) : IIdentityAccessStore, ITenantLicenseProvisioning
    {
        private Membership? _member; private AuditLog? _audit;
        public Task<User> UpsertGoogleUserAsync(User candidate, CancellationToken token)
        { var user = state.Users.Values.SingleOrDefault(value => value.GoogleSubject == candidate.GoogleSubject) ?? candidate; state.Users[user.Id] = user; return Task.FromResult(user); }
        public async Task<ITenantLicenseProvisioningScope?> BeginAsync(Guid tenant, CancellationToken token)
        {
            state.BeginCalls++; state.Entered?.TrySetResult(); if (state.Release is not null) await state.Release.Task.WaitAsync(token);
            return state.MissingTenant || tenant != state.License.TenantId ? null : new Scope(this, state);
        }
        public Task<bool> UserExistsAsync(Guid user, CancellationToken token) => Task.FromResult(state.Users.ContainsKey(user));
        public Task<bool> HasOwnerHistoryAsync(Guid tenant, CancellationToken token) => Task.FromResult(state.Members.Any(value => value.TenantId == tenant && value.Role == TenantRole.Owner));
        public Task<bool> HasActiveMembershipAsync(Guid tenant, Guid user, CancellationToken token) => Task.FromResult(state.Members.Any(value => value.TenantId == tenant && value.UserId == user && value.IsActive));
        public Task<int> CountActiveOwnersAsync(Guid tenant, CancellationToken token) => Task.FromResult(state.Members.Count(value => value.TenantId == tenant && value.Role == TenantRole.Owner && value.IsActive));
        public Task AddMembershipAsync(Membership member, AuditLog audit, CancellationToken token) { _member = member; _audit = audit; state.AfterWrite?.Invoke(); return Task.CompletedTask; }
        public Task<Membership?> FindMembershipAsync(Guid tenant, Guid member, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> FindBranchAssignmentsAsync(Guid tenant, Guid member, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkSchedule>> FindWorkScheduleAsync(Guid tenant, Guid member, CancellationToken token) => throw new NotSupportedException();
        public Task SaveDeactivationAsync(Membership member, AuditLog audit, CancellationToken token) => throw new NotSupportedException();
        public Task ReplaceBranchesAsync(Guid tenant, Guid member, IReadOnlyList<MembershipBranch> branches, AuditLog audit, CancellationToken token) => throw new NotSupportedException();
        public Task ReplaceScheduleAsync(Guid tenant, Guid member, IReadOnlyList<WorkSchedule> schedule, AuditLog audit, CancellationToken token) => throw new NotSupportedException();
        private sealed class Scope(Store store, State state) : ITenantLicenseProvisioningScope
        {
            public Guid TenantId => state.License.TenantId; public int MaxBranches => state.License.MaxBranches;
            public bool AllowsOperation(DateTimeOffset at) => state.License.AllowsOperation(at);
            public Task CompleteAsync(CancellationToken token)
            { Assert.NotNull(store._member); Assert.NotNull(store._audit); state.Members.Add(store._member); state.Audits.Add(store._audit); state.Commits++; return Task.CompletedTask; }
            public ValueTask DisposeAsync() { store._member = null; store._audit = null; return ValueTask.CompletedTask; }
        }
    }
    private sealed class Reader(State state) : IOperationalAccessReader
    {
        public Task<OperationalAccessSnapshot> ReadAsync(Guid user, Guid tenant, Guid? branch, CancellationToken token) =>
            Task.FromResult(new OperationalAccessSnapshot(true, state.Members.SingleOrDefault(value => value.TenantId == tenant && value.UserId == user), state.License, true, true, []));
    }
}
