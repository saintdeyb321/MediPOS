using MediPOS.Application.Tenancy;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MediPOS.UnitTests.Tenancy;

public sealed class AuditedSavepointTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task SaveDoesNotIntroduceImplicitSubtransactionsAndRestoresCallerSetting(bool asynchronous, bool failure, bool original)
    {
        var observer = new SaveObserver(failure);
        using var context = new MediPosDbContext(new DbContextOptionsBuilder<MediPosDbContext>()
            .UseNpgsql().AddInterceptors(observer).Options, new TenantDataContext());
        context.Database.AutoSavepointsEnabled = original;
        if (failure)
        {
            if (asynchronous) await Assert.ThrowsAsync<SaveFailure>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
            else Assert.Throws<SaveFailure>(() => context.SaveChanges());
        }
        else if (asynchronous) Assert.Equal(0, await context.SaveChangesAsync(TestContext.Current.CancellationToken));
        else Assert.Equal(0, context.SaveChanges());
        Assert.True(observer.Observed);
        Assert.Equal(original, context.Database.AutoSavepointsEnabled);
    }

    private sealed class SaveFailure : Exception;
    private sealed class SaveObserver(bool failure) : SaveChangesInterceptor
    {
        public bool Observed { get; private set; }
        private void Check(DbContextEventData data)
        {
            Observed = true;
            Assert.False(data.Context!.Database.AutoSavepointsEnabled);
            if (failure) throw new SaveFailure();
        }
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        { Check(eventData); return result; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Check(eventData); return ValueTask.FromResult(result); }
    }
}
