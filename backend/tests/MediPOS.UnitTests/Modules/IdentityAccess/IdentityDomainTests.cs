using MediPOS.Domain.Modules.IdentityAccess;

namespace MediPOS.UnitTests.Modules.IdentityAccess;

public sealed class IdentityDomainTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 6, 9, 0, 0, TimeSpan.FromHours(-5));

    [Fact]
    public void GoogleProfileUpdatesPreserveStableIdentity()
    {
        var user = User.Create(" sub-123 ", " first@example.test ", " First ", CreatedAt);
        var id = user.Id;
        user.UpdateGoogleProfile(" second@example.test ", " Second ");
        Assert.Equal("sub-123", user.GoogleSubject);
        Assert.Equal(id, user.Id);
        Assert.Equal(7, user.Id.Version);
        Assert.Equal("second@example.test", user.Email);
        Assert.Equal("Second", user.DisplayName);
        Assert.Equal(CreatedAt.ToUniversalTime(), user.CreatedAt);
        Assert.Equal(TimeSpan.Zero, user.CreatedAt.Offset);
    }

    [Theory]
    [InlineData("", "a@example.test", "A")]
    [InlineData(" ", "a@example.test", "A")]
    [InlineData("sub", "", "A")]
    [InlineData("sub", " ", "A")]
    [InlineData("sub", "a@example.test", "")]
    [InlineData("sub", "a@example.test", " ")]
    public void EmptyGoogleIdentityFieldsAreRejected(string subject, string email, string name) =>
        Assert.Throws<ArgumentException>(() => User.Create(subject, email, name, CreatedAt));

    [Theory]
    [InlineData(TenantRole.Owner, "owner")]
    [InlineData(TenantRole.Pharmacist, "pharmacist")]
    [InlineData(TenantRole.Cashier, "cashier")]
    public void MembershipUsesExplicitStableRoleCodes(TenantRole role, string code)
    {
        var membership = Membership.Create(Guid.NewGuid(), Guid.NewGuid(), role, CreatedAt);
        Assert.True(membership.IsActive);
        Assert.Equal(7, membership.Id.Version);
        Assert.Equal(code, TenantRoleCodes.ToCode(membership.Role));
        Assert.Equal(role, TenantRoleCodes.FromCode(code));
    }

    [Theory]
    [InlineData(true, false, TenantRole.Owner)]
    [InlineData(false, true, TenantRole.Owner)]
    [InlineData(false, false, (TenantRole)123)]
    public void MembershipRejectsMissingIdentifiersAndUnknownRole(bool emptyTenant, bool emptyUser, TenantRole role) =>
        Assert.ThrowsAny<ArgumentException>(() => Membership.Create(
            emptyTenant ? Guid.Empty : Guid.NewGuid(), emptyUser ? Guid.Empty : Guid.NewGuid(), role, CreatedAt));

    [Fact]
    public void DeactivationIsIdempotentAndKeepsIdentifiers()
    {
        var membership = Membership.Create(Guid.NewGuid(), Guid.NewGuid(), TenantRole.Cashier, CreatedAt);
        var id = membership.Id;
        membership.Deactivate(CreatedAt.AddHours(1));
        membership.Deactivate(CreatedAt.AddHours(2));
        Assert.False(membership.IsActive);
        Assert.Equal(id, membership.Id);
        Assert.Equal(CreatedAt.AddHours(1).ToUniversalTime(), membership.DeactivatedAt);
        Assert.Equal(TimeSpan.Zero, membership.DeactivatedAt!.Value.Offset);
    }

    [Fact]
    public void DeactivationCannotPrecedeCreation()
    {
        var membership = Membership.Create(Guid.NewGuid(), Guid.NewGuid(), TenantRole.Owner, CreatedAt);
        Assert.Throws<ArgumentOutOfRangeException>(() => membership.Deactivate(CreatedAt.AddTicks(-1)));
        Assert.True(membership.IsActive);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void BranchAssignmentCannotCrossEitherTenant(bool foreignMembership, bool foreignBranch)
    {
        var tenantId = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => MembershipBranch.Create(tenantId, Guid.NewGuid(),
            foreignMembership ? Guid.NewGuid() : tenantId, Guid.NewGuid(), foreignBranch ? Guid.NewGuid() : tenantId));
    }

    [Theory]
    [InlineData(9, 9)]
    [InlineData(18, 9)]
    public void InvalidAndOvernightWindowsAreRejected(int start, int end)
    {
        var tenantId = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() =>
            WorkSchedule.Create(tenantId, Guid.NewGuid(), tenantId, DayOfWeek.Monday, new(start, 0), new(end, 0)));
    }

    [Fact]
    public void ScheduleRejectsForeignMembershipUnknownDayAndSubMicrosecondPrecision()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() =>
            WorkSchedule.Create(tenantId, membershipId, Guid.NewGuid(), DayOfWeek.Monday, new(9, 0), new(18, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WorkSchedule.Create(tenantId, membershipId, tenantId, (DayOfWeek)20, new(9, 0), new(18, 0)));
        Assert.Throws<ArgumentException>(() =>
            WorkSchedule.Create(tenantId, membershipId, tenantId, DayOfWeek.Monday, new TimeOnly(9, 0).Add(TimeSpan.FromTicks(1)), new(18, 0)));
    }

    [Theory]
    [InlineData(8, 59, false)]
    [InlineData(9, 0, true)]
    [InlineData(17, 59, true)]
    [InlineData(18, 0, false)]
    public void WindowIncludesStartAndExcludesEnd(int hour, int minute, bool expected)
    {
        var tenantId = Guid.NewGuid();
        var window = WorkSchedule.Create(tenantId, Guid.NewGuid(), tenantId, DayOfWeek.Monday, new(9, 0), new(18, 0));
        Assert.Equal(expected, window.Contains(DayOfWeek.Monday, new(hour, minute)));
        Assert.False(window.Contains(DayOfWeek.Tuesday, new(hour, minute)));
    }
}
