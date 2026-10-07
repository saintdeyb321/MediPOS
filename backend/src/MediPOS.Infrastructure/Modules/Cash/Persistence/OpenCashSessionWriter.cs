using System.Data;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Cash;
using MediPOS.Application.Modules.Cash.OpenCashSession;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Cash;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediPOS.Infrastructure.Modules.Cash.Persistence;

internal sealed class OpenCashSessionWriter(MediPosDbContext context) : IOpenCashSessionWriter
{
    public async Task SaveAsync(CashSession session, AuditLog audit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(audit);
        context.SelectTenant(session.TenantId);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        try
        {
            context.CashSessions.Add(session);
            context.AddAudit(audit, session.TenantId, AuditAction.CashSessionOpened, session.Id);
            await context.SaveAuditedChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: CashSessionConfiguration.OpenSessionIndex })
        {
            context.ChangeTracker.Clear();
            throw new ApplicationErrorException(CashSessionErrors.AlreadyOpen);
        }
        catch
        {
            context.ChangeTracker.Clear();
            throw;
        }
    }
}
