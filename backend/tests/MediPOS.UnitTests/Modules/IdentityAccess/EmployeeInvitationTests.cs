using System.Security.Cryptography;
using MediPOS.Api.Authentication;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.Invitations;
using MediPOS.Domain.Modules.IdentityAccess;
using Microsoft.AspNetCore.DataProtection;

namespace MediPOS.UnitTests.Modules.IdentityAccess;

public sealed class EmployeeInvitationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 0, 0, 0, TimeSpan.Zero);
    private static EmployeeInvitation Invitation() => EmployeeInvitation.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), TenantRole.Cashier, SHA256.HashData("synthetic-invitation-secret"u8), Now);

    [Fact]
    public void OnlyHashIsRetainedAndAcceptanceBindsOneIdentityWithoutGrantingMembership()
    {
        var invitation = Invitation();
        var worker = Guid.NewGuid();
        Assert.Equal(32, invitation.SecretHash.Length);
        Assert.True(invitation.Accept(worker, Now.AddMinutes(1)));
        Assert.False(invitation.Accept(worker, Now.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => invitation.Accept(Guid.NewGuid(), Now.AddMinutes(2)));
        Assert.Equal(worker, invitation.AcceptedUserId);
        Assert.Null(invitation.ResultMembershipId);
        Assert.Equal(Now.AddMinutes(15), invitation.ExpiresAt);
        Assert.Equal(Now.AddMinutes(1), invitation.AcceptedAt);
    }

    [Theory]
    [InlineData(15)]
    [InlineData(16)]
    public void ExpiryIsExclusiveAndCannotBeExtendedByAccepting(int minutes)
    {
        var invitation = Invitation();
        Assert.Throws<InvalidOperationException>(() => invitation.Accept(Guid.NewGuid(), Now.AddMinutes(minutes)));
        Assert.Equal("expired", invitation.EffectiveStatus(Now.AddMinutes(minutes)));
        Assert.Null(invitation.AcceptedUserId);
    }

    [Fact]
    public void ApprovalRequiresAcceptanceAndDoesNotExpireTheCreatedMembership()
    {
        var invitation = Invitation();
        Assert.Throws<InvalidOperationException>(() => invitation.Approve(Guid.NewGuid(), 4, Now));
        invitation.Accept(Guid.NewGuid(), Now.AddMinutes(1));
        var membershipId = Guid.NewGuid();
        invitation.Approve(membershipId, 4, Now.AddMinutes(2));
        Assert.Equal(membershipId, invitation.ResultMembershipId);
        Assert.Equal(4L, invitation.ApprovedFromVersion);
        Assert.Equal("approved", invitation.EffectiveStatus(Now.AddYears(1)));
        Assert.Throws<InvalidOperationException>(() => invitation.Revoke(Guid.NewGuid(), "Must use membership deactivation", Now.AddMinutes(3)));
    }

    [Fact]
    public void AcceptedInvitationCannotBeApprovedAfterExpiryOrRevocation()
    {
        var expired = Invitation(); expired.Accept(Guid.NewGuid(), Now.AddMinutes(1));
        Assert.Throws<InvalidOperationException>(() => expired.Approve(Guid.NewGuid(), 1, Now.AddMinutes(15)));
        var revoked = Invitation(); revoked.Accept(Guid.NewGuid(), Now.AddMinutes(1)); revoked.Revoke(Guid.NewGuid(), "Wrong account", Now.AddMinutes(2));
        Assert.Throws<InvalidOperationException>(() => revoked.Approve(Guid.NewGuid(), 1, Now.AddMinutes(3)));
        Assert.Null(revoked.ResultMembershipId);
    }

    [Fact]
    public void OpaqueTicketIsPurposeAndKeyringBoundAndContainsNoRecoverableSecretInStorageModel()
    {
        var clock = new Clock();
        var protection = new EphemeralDataProtectionProvider(); // Unit-only, never production keys or OIDC evidence.
        var tickets = new EmployeeInvitationTickets(protection, clock);
        var secret = RandomNumberGenerator.GetBytes(32);
        var tenant = Guid.NewGuid(); var invitation = Guid.NewGuid();
        var ticket = tickets.Protect(tenant, invitation, secret, Now.AddMinutes(15));
        var command = tickets.Read(ticket);
        Assert.Equal(tenant, command.TenantId); Assert.Equal(invitation, command.InvitationId);
        Assert.Equal(SHA256.HashData(secret), command.SecretHash);
        Assert.DoesNotContain(Convert.ToBase64String(secret), ticket, StringComparison.Ordinal);
        Assert.Throws<ApplicationErrorException>(() => new EmployeeInvitationTickets(new EphemeralDataProtectionProvider(), clock).Read(ticket));
        var wrongPurpose = protection.CreateProtector("MediPOS.Authentication").Protect("synthetic");
        Assert.Throws<ApplicationErrorException>(() => tickets.Read(wrongPurpose));
        clock.Now = Now.AddMinutes(15);
        var expired = Assert.Throws<ApplicationErrorException>(() => tickets.Read(ticket));
        Assert.Equal(InvitationErrors.Unavailable, expired.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("forged-invitation")]
    public void InvalidTicketsFailWithOneSanitizedCode(string ticket)
    {
        var tickets = new EmployeeInvitationTickets(new EphemeralDataProtectionProvider(), new Clock());
        var error = Assert.Throws<ApplicationErrorException>(() => tickets.Read(ticket));
        Assert.Equal("invitations.unavailable", error.Error.Code);
        Assert.DoesNotContain(ticket + "forged", error.Message, StringComparison.Ordinal);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = EmployeeInvitationTests.Now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
