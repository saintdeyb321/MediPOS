using System.Security.Cryptography;
using System.Text.Json.Serialization;
using MediPOS.Api.Authentication;
using MediPOS.Application.Modules.IdentityAccess.Invitations;
using MediPOS.Domain.Modules.IdentityAccess;

namespace MediPOS.Api.Tenant;

public static partial class TenantEndpoints
{
    private static void MapEmployeeInvitations(RouteGroupBuilder api, RouteGroupBuilder tenants)
    {
        tenants.MapGet("/employee-invitations/{invitationId}", async (HttpContext c, EmployeeInvitationHandler handler) =>
            TenantHttp.Response(await handler.ReadAsync(TenantHttp.Tenant(c), TenantHttp.Id(c, "invitationId"), c.RequestAborted)))
            .Produces<EmployeeInvitationDetails>().WithName("tenant.employee_invitations.read");
        tenants.MapGet("/employee-invitations", async (HttpContext c, EmployeeInvitationHandler handler) =>
            TenantHttp.Response(await handler.ListAsync(TenantHttp.Tenant(c), TenantHttp.OptionalId(c, "clientRequestId"), TenantHttp.Offset(c), TenantHttp.Limit(c), c.RequestAborted)))
            .Produces<IReadOnlyList<EmployeeInvitationDetails>>().WithName("tenant.employee_invitations.list").WithQuery<EmployeeInvitationQuery>();
        tenants.MapPost("/employee-invitations", async (HttpContext c, EmployeeInvitationHandler handler, EmployeeInvitationTickets tickets) =>
        {
            var input = await TenantHttp.Read<CreateInvitationRequest>(c);
            var secret = RandomNumberGenerator.GetBytes(32);
            try
            {
                var result = await handler.CreateAsync(new(TenantHttp.Tenant(c), input.ClientRequestId, input.Role, SHA256.HashData(secret), TenantHttp.Actor(c)), c.RequestAborted);
                var ticket = result.Created ? tickets.Protect(result.Invitation.TenantId, result.Invitation.Id, secret, result.Invitation.ExpiresAt) : null;
                return TenantHttp.Response(new CreatedInvitationResponse(result.Invitation, ticket), result.Created ? 201 : 200);
            }
            finally { CryptographicOperations.ZeroMemory(secret); }
        }).Accepts<CreateInvitationRequest>("application/json").Produces<CreatedInvitationResponse>(201).Produces<CreatedInvitationResponse>().WithName("tenant.employee_invitations.create");
        tenants.MapPost("/employee-invitations/{invitationId}/approve", async (HttpContext c, EmployeeInvitationHandler handler) =>
        {
            var input = await TenantHttp.Read<ApproveInvitationRequest>(c);
            return TenantHttp.Response(await handler.ApproveAsync(TenantHttp.Tenant(c), TenantHttp.Id(c, "invitationId"), input.ExpectedVersion, input.Confirmed, c.RequestAborted));
        }).Accepts<ApproveInvitationRequest>("application/json").Produces<EmployeeInvitationDetails>().WithName("tenant.employee_invitations.approve");
        tenants.MapPost("/employee-invitations/{invitationId}/revoke", async (HttpContext c, EmployeeInvitationHandler handler) =>
        {
            var input = await TenantHttp.Read<RevokeInvitationRequest>(c);
            return TenantHttp.Response(await handler.RevokeAsync(TenantHttp.Tenant(c), TenantHttp.Id(c, "invitationId"), input.ExpectedVersion, input.Reason, c.RequestAborted));
        }).Accepts<RevokeInvitationRequest>("application/json").Produces<EmployeeInvitationDetails>().WithName("tenant.employee_invitations.revoke");
        // The same session/Origin/CSRF/rate boundary applies; membership does not exist yet.
        api.MapPost("/employee-invitations/accept", async (HttpContext c, EmployeeInvitationHandler handler, EmployeeInvitationTickets tickets) =>
        {
            var input = await TenantHttp.Read<AcceptInvitationRequest>(c);
            return TenantHttp.Response(await handler.AcceptAsync(tickets.Read(input.Ticket), c.RequestAborted));
        }).Accepts<AcceptInvitationRequest>("application/json").Produces<InvitationAcceptance>().WithName("tenant.employee_invitations.accept");
    }
    public sealed class EmployeeInvitationQuery : TenantOpenApi.Page { public Guid? ClientRequestId { get; init; } }
    public sealed record CreateInvitationRequest([property: JsonRequired] TenantRole Role, [property: JsonRequired] Guid ClientRequestId);
    public sealed record ApproveInvitationRequest([property: JsonRequired] uint ExpectedVersion, [property: JsonRequired] bool Confirmed);
    public sealed record RevokeInvitationRequest([property: JsonRequired] uint ExpectedVersion, [property: JsonRequired] string Reason);
    public sealed record AcceptInvitationRequest([property: JsonRequired] string Ticket);
    public sealed record CreatedInvitationResponse(EmployeeInvitationDetails Invitation, string? Ticket);
}
