using MediPOS.Application.Modules.Branches;
using MediPOS.Application.Modules.IdentityAccess;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.CreateMembership;
using MediPOS.Application.Modules.IdentityAccess.DeactivateMembership;
using MediPOS.Application.Modules.IdentityAccess.ReplaceWorkSchedule;
using MediPOS.Application.Modules.IdentityAccess.SetMembershipBranches;
using MediPOS.Application.Modules.IdentityAccess.UpsertGoogleUser;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.Branches;
using MediPOS.Domain.Modules.IdentityAccess;

namespace MediPOS.UnitTests.Modules.IdentityAccess;

public sealed class IdentityApplicationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 14, 0, 0, TimeSpan.Zero);
    private static readonly string[] CreationSteps = ["lock", "user", "duplicate", "owners", "insert", "commit", "dispose"];

    [Fact]
    public async Task GoogleUpsertUsesVerifiedSubjectAndPreservesIdentityWhenEmailChanges()
    {
        var store = new RecordingStore();
        var first = await new UpsertGoogleUserHandler(new VerifiedSource(new("stable-sub", "first@example.test", "First")), store, new Clock())
            .HandleAsync(TestContext.Current.CancellationToken);
        var second = await new UpsertGoogleUserHandler(new VerifiedSource(new("stable-sub", "second@example.test", "Second")), store, new Clock())
            .HandleAsync(TestContext.Current.CancellationToken);
        var other = await new UpsertGoogleUserHandler(new VerifiedSource(new("other-sub", "second@example.test", "Other")), store, new Clock())
            .HandleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(first.Id, second.Id);
        Assert.NotEqual(second.Id, other.Id);
        Assert.Equal("second@example.test", second.Email);
        Assert.Equal("Second", second.DisplayName);
    }

    [Fact]
    public async Task MissingVerifiedIdentityCannotCreateUser()
    {
        var store = new RecordingStore();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new UpsertGoogleUserHandler(new VerifiedSource(null), store, new Clock()).HandleAsync(TestContext.Current.CancellationToken));
        Assert.Empty(store.Users);
    }

    [Fact]
    public async Task MembershipCountsAndInsertAreInsideTenantScope()
    {
        var store = new RecordingStore();
        var provisioning = new RecordingProvisioning(store);
        var result = await new CreateMembershipHandler(store, provisioning, new Clock()).HandleAsync(
            new(store.TenantId, store.UserId, TenantRole.Owner), TestContext.Current.CancellationToken);
        Assert.Equal(CreationSteps, store.Steps);
        Assert.Equal(Now, result.CreatedAt);
        Assert.True(result.IsActive);
    }

    [Fact]
    public async Task DuplicateActiveMembershipIsRejectedAndScopeIsDisposedWithoutCommit()
    {
        var store = new RecordingStore();
        store.Memberships.Add(Membership.Create(store.TenantId, store.UserId, TenantRole.Cashier, Now));
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateHandler(store).HandleAsync(
            new(store.TenantId, store.UserId, TenantRole.Owner), TestContext.Current.CancellationToken));
        Assert.Single(store.Memberships);
        Assert.DoesNotContain("commit", store.Steps);
        Assert.Equal("dispose", store.Steps[^1]);
    }

    [Fact]
    public async Task ExactlyTwoOwnersAreAllowedAndThirdIsRejected()
    {
        var store = new RecordingStore();
        var handler = CreateHandler(store);
        await handler.HandleAsync(new(store.TenantId, Guid.NewGuid(), TenantRole.Owner), TestContext.Current.CancellationToken);
        await handler.HandleAsync(new(store.TenantId, Guid.NewGuid(), TenantRole.Owner), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(
            new(store.TenantId, Guid.NewGuid(), TenantRole.Owner), TestContext.Current.CancellationToken));
        Assert.Equal(2, store.Memberships.Count);
        await handler.HandleAsync(new(store.TenantId, Guid.NewGuid(), TenantRole.Pharmacist), TestContext.Current.CancellationToken);
        Assert.Equal(3, store.Memberships.Count);
    }

    [Fact]
    public async Task InactiveOwnerDoesNotConsumeOwnerSlotOrBlockNewMembership()
    {
        var store = new RecordingStore();
        var inactive = Membership.Create(store.TenantId, store.UserId, TenantRole.Owner, Now);
        inactive.Deactivate(Now);
        store.Memberships.Add(inactive);
        store.Memberships.Add(Membership.Create(store.TenantId, Guid.NewGuid(), TenantRole.Owner, Now));
        var result = await CreateHandler(store).HandleAsync(new(store.TenantId, store.UserId, TenantRole.Owner), TestContext.Current.CancellationToken);
        Assert.NotEqual(inactive.Id, result.Id);
        Assert.False(inactive.IsActive);
        Assert.Equal(2, store.Memberships.Count(value => value.Role == TenantRole.Owner && value.IsActive));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingTenantOrUserCannotCreateMembership(bool missingTenant)
    {
        var store = new RecordingStore { UserExists = missingTenant };
        var provisioning = new RecordingProvisioning(store) { Missing = missingTenant };
        await Assert.ThrowsAsync<KeyNotFoundException>(() => new CreateMembershipHandler(store, provisioning, new Clock()).HandleAsync(
            new(store.TenantId, store.UserId, TenantRole.Owner), TestContext.Current.CancellationToken));
        Assert.Empty(store.Memberships);
        Assert.DoesNotContain("commit", store.Steps);
    }

    [Fact]
    public async Task BranchReplacementValidatesEntireSelectionBeforeWriting()
    {
        var store = WithMembership();
        var branches = new BranchLookup();
        var branch = Branch.Create(store.TenantId, Guid.NewGuid(), store.TenantId, "Centro", Now);
        branches.Values.Add(branch);
        store.Assignments.Add(MembershipBranch.Create(store.TenantId, store.Memberships[0].Id, store.TenantId, branch.Id, store.TenantId));
        var handler = new SetMembershipBranchesHandler(store, branches, new RecordingProvisioning(store));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => handler.HandleAsync(
            new(store.TenantId, store.Memberships[0].Id, [branch.Id, Guid.NewGuid()]), TestContext.Current.CancellationToken));
        Assert.Single(store.Assignments);
        Assert.DoesNotContain("replace_branches", store.Steps);
        Assert.DoesNotContain("commit", store.Steps);
    }

    [Fact]
    public async Task BranchReplacementAllowsMultipleBranchesAndEmptySelectionAtomically()
    {
        var store = WithMembership();
        var branches = new BranchLookup();
        branches.Values.Add(Branch.Create(store.TenantId, Guid.NewGuid(), store.TenantId, "Centro", Now));
        branches.Values.Add(Branch.Create(store.TenantId, Guid.NewGuid(), store.TenantId, "Norte", Now));
        var handler = new SetMembershipBranchesHandler(store, branches, new RecordingProvisioning(store));
        await handler.HandleAsync(new(store.TenantId, store.Memberships[0].Id, branches.Values.Select(value => value.Id).ToArray()),
            TestContext.Current.CancellationToken);
        Assert.Equal(2, store.Assignments.Count);
        Assert.Contains("commit", store.Steps);
        await handler.HandleAsync(new(store.TenantId, store.Memberships[0].Id, []), TestContext.Current.CancellationToken);
        Assert.Empty(store.Assignments);
    }

    [Fact]
    public async Task DuplicateBranchesAndSchedulesAreRejectedBeforeMutation()
    {
        var store = WithMembership();
        var membershipId = store.Memberships[0].Id;
        var branchId = Guid.NewGuid();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new SetMembershipBranchesHandler(store, new BranchLookup(), new RecordingProvisioning(store)).HandleAsync(
                new(store.TenantId, membershipId, [branchId, branchId]), TestContext.Current.CancellationToken));
        var window = new WorkWindow(DayOfWeek.Tuesday, new(9, 0), new(18, 0));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new ReplaceWorkScheduleHandler(store, new RecordingProvisioning(store)).HandleAsync(
                new(store.TenantId, membershipId, [window, window]), TestContext.Current.CancellationToken));
        Assert.Empty(store.Steps);
    }

    [Fact]
    public async Task InvalidScheduleLeavesExistingScheduleIntact()
    {
        var store = WithMembership();
        var membership = store.Memberships[0];
        store.Schedule.Add(WorkSchedule.Create(store.TenantId, membership.Id, store.TenantId, DayOfWeek.Tuesday, new(9, 0), new(18, 0)));
        await Assert.ThrowsAsync<ArgumentException>(() => new ReplaceWorkScheduleHandler(store, new RecordingProvisioning(store))
            .HandleAsync(new(store.TenantId, membership.Id,
                [new(DayOfWeek.Monday, new(9, 0), new(18, 0)), new(DayOfWeek.Tuesday, new(18, 0), new(9, 0))]), TestContext.Current.CancellationToken));
        Assert.Single(store.Schedule);
        Assert.DoesNotContain("replace_schedule", store.Steps);
    }

    [Fact]
    public async Task ScheduleReplacementAllowsMultipleDailyWindowsAndClearing()
    {
        var store = WithMembership();
        var handler = new ReplaceWorkScheduleHandler(store, new RecordingProvisioning(store));
        await handler.HandleAsync(new(store.TenantId, store.Memberships[0].Id,
            [new(DayOfWeek.Tuesday, new(9, 0), new(12, 0)), new(DayOfWeek.Tuesday, new(14, 0), new(18, 0)),
             new(DayOfWeek.Wednesday, new(9, 0), new(18, 0))]), TestContext.Current.CancellationToken);
        Assert.Equal(3, store.Schedule.Count);
        Assert.Contains("commit", store.Steps);
        await handler.HandleAsync(new(store.TenantId, store.Memberships[0].Id, []), TestContext.Current.CancellationToken);
        Assert.Empty(store.Schedule);
    }

    [Fact]
    public async Task DeactivationPreservesAssignmentsAndScheduleAndRejectsFurtherReplacement()
    {
        var store = WithMembership();
        var membership = store.Memberships[0];
        store.Assignments.Add(MembershipBranch.Create(store.TenantId, membership.Id, store.TenantId, Guid.NewGuid(), store.TenantId));
        store.Schedule.Add(WorkSchedule.Create(store.TenantId, membership.Id, store.TenantId, DayOfWeek.Tuesday, new(9, 0), new(18, 0)));
        var provisioning = new RecordingProvisioning(store);
        var handler = new DeactivateMembershipHandler(store, provisioning, new Clock());
        var result = await handler.HandleAsync(new(store.TenantId, membership.Id), TestContext.Current.CancellationToken);
        Assert.False(result.IsActive);
        Assert.Equal(Now, result.DeactivatedAt);
        await handler.HandleAsync(new(store.TenantId, membership.Id), TestContext.Current.CancellationToken);
        Assert.Single(store.Memberships);
        Assert.Single(store.Assignments);
        Assert.Single(store.Schedule);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SetMembershipBranchesHandler(store, new BranchLookup(), provisioning).HandleAsync(
                new(store.TenantId, membership.Id, []), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ReplaceWorkScheduleHandler(store, provisioning).HandleAsync(
                new(store.TenantId, membership.Id, []), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ForeignTenantMembershipCannotBeDeactivated()
    {
        var store = WithMembership();
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            new DeactivateMembershipHandler(store, new RecordingProvisioning(store), new Clock()).HandleAsync(
                new(Guid.NewGuid(), store.Memberships[0].Id), TestContext.Current.CancellationToken));
        Assert.True(store.Memberships[0].IsActive);
        Assert.DoesNotContain("commit", store.Steps);
    }

    private static CreateMembershipHandler CreateHandler(RecordingStore store) => new(store, new RecordingProvisioning(store), new Clock());

    private static RecordingStore WithMembership()
    {
        var store = new RecordingStore();
        store.Memberships.Add(Membership.Create(store.TenantId, store.UserId, TenantRole.Cashier, Now.AddDays(-1)));
        return store;
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class VerifiedSource(VerifiedGoogleIdentity? identity) : IVerifiedGoogleIdentitySource
    {
        public Task<VerifiedGoogleIdentity?> GetVerifiedIdentityAsync(CancellationToken cancellationToken) => Task.FromResult(identity);
    }

    // Recording ports test application sequencing, not PostgreSQL concurrency or constraints.
    private sealed class RecordingStore : IIdentityAccessStore
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public Guid UserId { get; } = Guid.NewGuid();
        public bool UserExists { get; init; } = true;
        public bool InScope { get; set; }
        public List<string> Steps { get; } = [];
        public List<User> Users { get; } = [];
        public List<Membership> Memberships { get; } = [];
        public List<MembershipBranch> Assignments { get; } = [];
        public List<WorkSchedule> Schedule { get; } = [];

        public Task<User> UpsertGoogleUserAsync(User user, CancellationToken cancellationToken)
        {
            var existing = Users.SingleOrDefault(value => value.GoogleSubject == user.GoogleSubject);
            if (existing is null)
            {
                Users.Add(user);
                return Task.FromResult(user);
            }
            existing.UpdateGoogleProfile(user.Email, user.DisplayName);
            return Task.FromResult(existing);
        }
        public Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken)
        {
            Steps.Add("user");
            return Task.FromResult(UserExists);
        }
        public Task<bool> HasActiveMembershipAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
        {
            Assert.True(InScope);
            Steps.Add("duplicate");
            return Task.FromResult(Memberships.Any(value => value.TenantId == tenantId && value.UserId == userId && value.IsActive));
        }
        public Task<int> CountActiveOwnersAsync(Guid tenantId, CancellationToken cancellationToken)
        {
            Assert.True(InScope);
            Steps.Add("owners");
            return Task.FromResult(Memberships.Count(value => value.TenantId == tenantId && value.IsActive && value.Role == TenantRole.Owner));
        }
        public Task<Membership?> FindMembershipAsync(Guid tenantId, Guid membershipId, CancellationToken cancellationToken) =>
            Task.FromResult(Memberships.SingleOrDefault(value => value.TenantId == tenantId && value.Id == membershipId));
        public Task AddMembershipAsync(Membership membership, CancellationToken cancellationToken)
        {
            Assert.True(InScope);
            Steps.Add("insert");
            Memberships.Add(membership);
            return Task.CompletedTask;
        }
        public Task SaveDeactivationAsync(Membership membership, CancellationToken cancellationToken)
        {
            Assert.True(InScope);
            Steps.Add("deactivate");
            return Task.CompletedTask;
        }
        public Task ReplaceBranchesAsync(Guid tenantId, Guid membershipId, IReadOnlyList<MembershipBranch> branches, CancellationToken cancellationToken)
        {
            Assert.True(InScope);
            Steps.Add("replace_branches");
            Assignments.Clear();
            Assignments.AddRange(branches);
            return Task.CompletedTask;
        }
        public Task ReplaceScheduleAsync(Guid tenantId, Guid membershipId, IReadOnlyList<WorkSchedule> schedule, CancellationToken cancellationToken)
        {
            Assert.True(InScope);
            Steps.Add("replace_schedule");
            Schedule.Clear();
            Schedule.AddRange(schedule);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingProvisioning(RecordingStore store) : ITenantLicenseProvisioning
    {
        public bool Missing { get; init; }
        public Task<ITenantLicenseProvisioningScope?> BeginAsync(Guid tenantId, CancellationToken cancellationToken)
        {
            store.Steps.Add("lock");
            store.InScope = !Missing;
            return Task.FromResult<ITenantLicenseProvisioningScope?>(Missing ? null : new Scope(store, tenantId));
        }

        private sealed class Scope(RecordingStore store, Guid tenantId) : ITenantLicenseProvisioningScope
        {
            public Guid TenantId => tenantId;
            public int MaxBranches => 3;
            public bool AllowsOperation(DateTimeOffset at) => true;
            public Task CompleteAsync(CancellationToken cancellationToken)
            {
                store.Steps.Add("commit");
                return Task.CompletedTask;
            }
            public ValueTask DisposeAsync()
            {
                store.InScope = false;
                store.Steps.Add("dispose");
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class BranchLookup : IBranchesStore
    {
        public List<Branch> Values { get; } = [];
        public Task<Branch?> FindBranchAsync(Guid tenantId, Guid branchId, CancellationToken cancellationToken) =>
            Task.FromResult(Values.SingleOrDefault(value => value.TenantId == tenantId && value.Id == branchId));
        public Task<bool> TenantExistsAsync(Guid tenantId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<LegalEntity?> FindLegalEntityAsync(Guid tenantId, Guid legalEntityId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Branch?> FindMainHubBranchAsync(Guid tenantId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CountBranchesAsync(Guid tenantId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddLegalEntityAsync(LegalEntity legalEntity, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddBranchAsync(Branch branch, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ReplaceMainHubAsync(Branch branch, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
