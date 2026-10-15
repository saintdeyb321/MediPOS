using System.Reflection;
using MediPOS.Application.Modules.Commissions;
using MediPOS.Application.Modules.Reporting.GetOwnerCommissionsReport;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Commissions;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.UnitTests.Api;

public sealed partial class TenantHttpTestState
{
    internal TenantCommissionSettings? CommissionSettings { get; set; }
    internal List<CommissionRule> CommissionRules { get; } = [];
    internal List<CommissionEntry> CommissionEntries { get; } = [];
    private void ConfigureCommissions(IServiceCollection services)
    {
        Type[] ports = [typeof(ICommissionConfigurationStore), typeof(IOwnerCommissionsReportReader)];
        foreach (var port in ports)
            services.AddScoped(port, _ => { var proxy = DispatchProxy.Create(port, typeof(Port)); ((Port)proxy).State = this; return proxy; });
    }
    private bool InvokeCommissions(Type type, string method, object?[] args, out object? result)
    {
        result = null;
        if (type == typeof(ICommissionConfigurationStore))
        {
            result = method == "BeginAsync" ? Task.FromResult<ICommissionConfigurationScope>(new CommissionScope(this)) :
                Task.FromResult<IReadOnlyList<CommissionRule>>(CommissionRules.Where(rule => (args[1] is null || rule.BusinessProductId == (Guid)args[1]!) && ((bool)args[2]! || rule.IsActive)).Skip((int)args[3]!).Take((int)args[4]!).ToArray()); return true;
        }
        if (type == typeof(IOwnerCommissionsReportReader))
        {
            var request = (OwnerCommissionsReadRequest)args[0]!;
            var rows = CommissionEntries.Where(entry => entry.EntryType == CommissionEntryType.Earned && entry.OccurredAt >= request.PeriodStartUtc && entry.OccurredAt < request.PeriodEndExclusiveUtc)
                .Select(entry =>
                {
                    var sale = Sales.Single(s => s.Id == entry.SaleId); var reversal = CommissionEntries.SingleOrDefault(r => r.ReversesCommissionEntryId == entry.Id);
                    return new CommissionReportRow(entry.Id, sale.Id, entry.SaleLineId, sale.BranchId, entry.SellerMembershipId, User.DisplayName, entry.BusinessProductId, Products.Single(p => p.Id == entry.BusinessProductId).Name, entry.OccurredAt, entry.Amount, -(reversal?.Amount ?? 0m), entry.Amount + (reversal?.Amount ?? 0m), reversal?.OccurredAt);
                }).Where(row => (!request.BranchId.HasValue || row.BranchId == request.BranchId) && (!request.SellerMembershipId.HasValue || row.SellerMembershipId == request.SellerMembershipId) && (!request.BusinessProductId.HasValue || row.BusinessProductId == request.BusinessProductId)).ToArray();
            result = Task.FromResult(new CommissionReportPage(rows.Skip(request.Offset).Take(request.Limit).ToArray(), new(rows.LongLength, rows.Sum(r => r.OriginalEarnedAmount), rows.Sum(r => r.CompensatedAmount), rows.Sum(r => r.CurrentNetAmount)))); return true;
        }
        return false;
    }
    private sealed class CommissionScope(TenantHttpTestState state) : ICommissionConfigurationScope
    {
        public Guid TenantId => state.TenantId;
        public Task<TenantCommissionSettings?> LoadSettingsAsync(CancellationToken cancellationToken) => Task.FromResult(state.CommissionSettings);
        public Task<BusinessProduct?> LoadProductAsync(Guid productId, CancellationToken cancellationToken) => Task.FromResult(state.Products.SingleOrDefault(p => p.Id == productId));
        public Task<CommissionRule?> LoadActiveRuleAsync(Guid productId, CancellationToken cancellationToken) => Task.FromResult(state.CommissionRules.SingleOrDefault(rule => rule.BusinessProductId == productId && rule.IsActive));
        public Task<CommissionRule?> LoadRuleAsync(Guid ruleId, CancellationToken cancellationToken) => Task.FromResult(state.CommissionRules.SingleOrDefault(rule => rule.Id == ruleId));
        public Task PersistSettingsAsync(TenantCommissionSettings settings, AuditLog audit, CancellationToken cancellationToken) => Save(() => { state.CommissionSettings = settings; state.Audits.Add(audit); });
        public Task ReplaceRuleAsync(CommissionRule? previous, CommissionRule replacement, AuditLog? deactivationAudit, AuditLog creationAudit, CancellationToken cancellationToken) => Save(() => { if (deactivationAudit is not null) state.Audits.Add(deactivationAudit); state.CommissionRules.Add(replacement); state.Audits.Add(creationAudit); });
        public Task PersistDeactivationAsync(CommissionRule rule, AuditLog audit, CancellationToken cancellationToken) => Save(() => state.Audits.Add(audit));
        public Task CompleteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
