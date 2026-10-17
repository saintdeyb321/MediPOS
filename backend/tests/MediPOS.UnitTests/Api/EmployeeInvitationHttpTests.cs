using System.Net;
using MediPOS.Domain.Modules.IdentityAccess;
using static MediPOS.UnitTests.Api.TenantApiHttpTests;

namespace MediPOS.UnitTests.Api;

[Collection("Observability")]
public sealed class EmployeeInvitationHttpTests
{
    [Theory]
    [InlineData("create", false, "auth.csrf_invalid")]
    [InlineData("accept", false, "auth.csrf_invalid")]
    [InlineData("accept", true, "invitations.unavailable")]
    public async Task InvitationBoundaryRejectsCsrfOrInvalidTicketWithoutMembershipWrites(string operation, bool csrf, string expected)
    {
        var state = SeedBranch(); await using var host = await Host(state);
        await PlatformLicenseHttpTests.Login(host);
        var path = operation == "accept" ? "/api/employee-invitations/accept" : $"/api/tenants/{state.TenantId:D}/employee-invitations";
        object input = operation == "accept" ? new { ticket = "invalid-not-a-protected-ticket" } : new { role = "cashier", clientRequestId = Guid.NewGuid() };
        using var response = await Send(host, "POST", path, input, csrf ? await host.Csrf() : null);
        await Problem(response, csrf ? 403 : 400, expected);
        Assert.Empty(state.Employees); Assert.Empty(state.Audits);
    }

    [Theory]
    [InlineData(TenantRole.Cashier)]
    [InlineData(TenantRole.Pharmacist)]
    public async Task WorkerCannotCreateInvitationsOrGrantArbitraryMembership(TenantRole role)
    {
        var state = SeedBranch();
        state.Member = Membership.Create(state.TenantId, state.User.Id, role, DateTimeOffset.UtcNow);
        await using var host = await Host(state); await PlatformLicenseHttpTests.Login(host);
        using var response = await Send(host, "POST", $"/api/tenants/{state.TenantId:D}/employee-invitations", new { role = "owner", clientRequestId = Guid.NewGuid() }, await host.Csrf());
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode); Assert.Empty(state.Employees); Assert.Empty(state.Audits);
    }
}
