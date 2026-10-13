using System.Net;
using MediPOS.Api.Authentication;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.TenancyLicensing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using GoogleHost = MediPOS.UnitTests.Api.GoogleAuthenticationHttpTests.Host;

namespace MediPOS.UnitTests.Api;

[Collection("Observability")]
public sealed class PlatformAdministrationHttpTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly string[] Routes = ["/auth/csrf", "/auth/google/callback", "/auth/login", "/auth/logout", "/auth/me", "/health", "/health/ready"];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SessionAuthorityIsIndependentOfOwnerAndMembershipDeactivation(bool grant)
    {
        var user = User.Create("subject-one", "same@example.test", "Synthetic operator", DateTimeOffset.UtcNow);
        var tenant = Guid.NewGuid(); var license = License.Create(tenant, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), 3,
            LicenseStatus.Active, Guid.NewGuid(), DateTimeOffset.UtcNow);
        var reader = new Reader(new(true, null, license, true, true, []));
        await using var host = await Host(user, grant ? user.Id : Guid.NewGuid(), reader: reader);
        Assert.Equal("denied", await host.Client.GetStringAsync("/__platform", Token));
        var flow = await host.StartLogin(); using var callback = await host.Callback(flow); Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        var expected = grant ? user.Id.ToString("D") : "denied";
        Assert.Equal(expected, await host.Client.GetStringAsync("/__platform?userId=" + Guid.NewGuid() + "&ActorId=" + Guid.NewGuid() + "&TenantId=" + tenant + "&role=Superadmin&email=same@example.test&sub=subject-one", Token));
        Assert.Equal("access.membership_missing", await host.Client.GetStringAsync("/__tenant?tenant=" + tenant, Token));
        var membership = Membership.Create(tenant, user.Id, TenantRole.Owner, DateTimeOffset.UtcNow);
        reader.Snapshot = reader.Snapshot with { Membership = membership };
        Assert.Equal("allowed", await host.Client.GetStringAsync("/__tenant?tenant=" + tenant, Token));
        Assert.Equal(expected, await host.Client.GetStringAsync("/__platform", Token));
        license.Suspend(user.Id, DateTimeOffset.UtcNow);
        Assert.Equal("access.license_denied", await host.Client.GetStringAsync("/__tenant?tenant=" + tenant, Token));
        Assert.Equal(expected, await host.Client.GetStringAsync("/__platform", Token));
        license.Reactivate(LicenseStatus.Active, user.Id, DateTimeOffset.UtcNow);
        membership.Deactivate(DateTimeOffset.UtcNow);
        Assert.Equal("access.membership_inactive", await host.Client.GetStringAsync("/__tenant?tenant=" + tenant, Token));
        Assert.Equal(expected, await host.Client.GetStringAsync("/__platform", Token));
        using var logout = await host.Post("/auth/logout", await host.Csrf()); Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal("denied", await host.Client.GetStringAsync("/__platform", Token));
    }

    [Fact]
    public async Task ASecondGoogleSubjectWithSameEmailNeverInheritsTheApprovedInternalUserGrant()
    {
        var user = User.Create("subject-one", "same@example.test", "Synthetic operator", DateTimeOffset.UtcNow);
        await using var host = await Host(user, user.Id);
        var first = await host.StartLogin(); using var accepted = await host.Callback(first);
        Assert.Equal(user.Id.ToString("D"), await host.Client.GetStringAsync("/__platform", Token));
        host.Provider.Subject = "subject-two";
        var second = await host.StartLogin(); using var callback = await host.Callback(second);
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.NotEqual(user.Id, host.Users.BySubject["subject-two"].Id);
        Assert.Equal(user.Email, host.Users.BySubject["subject-two"].Email);
        Assert.Equal("denied", await host.Client.GetStringAsync("/__platform", Token));
    }

    [Theory]
    [InlineData("oidc-disabled")]
    [InlineData("development")]
    [InlineData("missing-ids")]
    [InlineData("invalid-id")]
    [InlineData("duplicate")]
    public async Task InvalidPlatformCompositionCannotPartiallyActivate(string defect)
    {
        await using var host = await GoogleHost.CreateAsync();
        var settings = host.App.Services.GetRequiredService<GoogleAuthenticationSettings>();
        var config = GoogleHost.Configuration(settings.KeyRingPath, settings.CertificatePath, settings.CertificatePassword);
        var id = Guid.NewGuid(); Enable(config, id);
        switch (defect)
        {
            case "oidc-disabled": config["Authentication:Google:Enabled"] = "false"; break;
            case "development": config["environment"] = "Development"; break;
            case "missing-ids": config.Remove("PlatformAdministration:AdministratorUserIds:0"); break;
            case "invalid-id": config["PlatformAdministration:AdministratorUserIds:0"] = "operator-private@example.test"; break;
            case "duplicate": config["PlatformAdministration:AdministratorUserIds:1"] = id.ToString("D"); break;
        }
        var error = Assert.Throws<InvalidOperationException>(() => global::Program.CreateApplication(config.Select(pair => "--" + pair.Key + "=" + pair.Value).ToArray()));
        Assert.DoesNotContain(id.ToString("D"), error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("operator-private", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EffectiveRestartWithRemovedGrantDeniesAFreshValidatedSession()
    {
        var user = User.Create("subject-one", "same@example.test", "Synthetic operator", DateTimeOffset.UtcNow);
        await using (var granted = await Host(user, user.Id))
        {
            var flow = await granted.StartLogin(); using var callback = await granted.Callback(flow);
            Assert.Equal(user.Id.ToString("D"), await granted.Client.GetStringAsync("/__platform", Token));
        }
        await using var revoked = await Host(user, Guid.NewGuid());
        var newFlow = await revoked.StartLogin(); using var newCallback = await revoked.Callback(newFlow);
        using var me = await revoked.Client.GetAsync("/auth/me", Token); Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal("denied", await revoked.Client.GetStringAsync("/__platform", Token));
    }

    [Fact]
    public async Task ExpiredServerTicketCannotProvidePlatformAuthority()
    {
        var user = User.Create("subject-one", "same@example.test", "Synthetic operator", DateTimeOffset.UtcNow);
        var clock = new Clock();
        await using var host = await Host(user, user.Id, clock: clock);
        var flow = await host.StartLogin(); using var callback = await host.Callback(flow);
        Assert.Equal(user.Id.ToString("D"), await host.Client.GetStringAsync("/__platform", Token));
        clock.Now = DateTimeOffset.UtcNow.AddHours(2);
        Assert.Equal("denied", await host.Client.GetStringAsync("/__platform", Token));
        using var me = await host.Client.GetAsync("/auth/me", Token); Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProductionCompositionPublishesNoNewRoutesAndKeepsAuthorityDisabledByDefault(bool activate)
    {
        var user = User.Create("subject-one", "same@example.test", "Synthetic operator", DateTimeOffset.UtcNow);
        await using var host = await GoogleHost.CreateAsync(configure: config =>
        {
            if (activate) Enable(config, user.Id);
        }, seedUsers: users => users.BySubject[user.GoogleSubject] = user);
        Assert.Equal(Routes, ((IEndpointRouteBuilder)host.App).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText).Order().ToArray());
        using var scope = host.App.Services.CreateScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<IPlatformAdministration>().GetAuthorizedActorAsync(Token));
        Assert.Equal(activate, scope.ServiceProvider.GetRequiredService<IPlatformAdministration>() is SessionPlatformAdministration);
        var flow = await host.StartLogin(); using var callback = await host.Callback(flow);
        using var absent = await host.Client.GetAsync("/platform/tenants", Token); Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
    }

    [Fact]
    public async Task LogsNeverSerializeApprovedOperatorsOrProtectedConfiguration()
    {
        var previous = Console.Out; using var captured = new StringWriter(); Console.SetOut(TextWriter.Synchronized(captured));
        var user = User.Create("subject-one", "same@example.test", "Synthetic operator", DateTimeOffset.UtcNow);
        try
        {
            await using var host = await Host(user, user.Id);
            var flow = await host.StartLogin(); using var callback = await host.Callback(flow);
            Assert.Equal(user.Id.ToString("D"), await host.Client.GetStringAsync("/__platform", Token));
        }
        finally { Console.SetOut(previous); }
        var logs = captured.ToString();
        Assert.False(string.IsNullOrWhiteSpace(logs));
        Assert.DoesNotContain(user.Id.ToString("D"), logs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(user.GoogleSubject, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(user.Email, logs, StringComparison.Ordinal);
        Assert.DoesNotContain("AdministratorUserIds", logs, StringComparison.Ordinal);
    }

    private static Task<GoogleHost> Host(User user, Guid approved, Reader? reader = null, Clock? clock = null) => GoogleHost.CreateAsync(app =>
    {
        // Test-only probes: production composition above asserts that these routes do not exist.
        app.MapGet("/__platform", async (IPlatformAdministration administration) =>
            (await administration.GetAuthorizedActorAsync(Token))?.ToString("D") ?? "denied");
        if (reader is not null) app.MapGet("/__tenant", async (HttpContext context, Guid tenant) =>
        {
            var authorizer = new TenantOperationAuthorizer(new(context.RequestServices.GetRequiredService<IAuthenticatedMediPosUser>(), reader, new TenantDataContext()), TimeProvider.System);
            try { await authorizer.RequireAsync(tenant, null, null, TenantOperation.Administration, Token); return "allowed"; }
            catch (ApplicationErrorException error) { return error.Error.Code; }
        });
    }, configure: config => Enable(config, approved), seedUsers: users => users.BySubject[user.GoogleSubject] = user,
        configureServices: services => { if (clock is not null) services.AddSingleton<TimeProvider>(clock); });
    private static void Enable(Dictionary<string, string> config, Guid id)
    {
        config["PlatformAdministration:Enabled"] = "true";
        config["PlatformAdministration:AdministratorUserIds:0"] = id.ToString("D");
    }
    private sealed class Reader(OperationalAccessSnapshot snapshot) : IOperationalAccessReader
    {
        internal OperationalAccessSnapshot Snapshot = snapshot;
        public Task<OperationalAccessSnapshot> ReadAsync(Guid userId, Guid tenantId, Guid? branchId, CancellationToken token) => Task.FromResult(Snapshot);
    }
    private sealed class Clock : TimeProvider
    { internal DateTimeOffset Now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
}
