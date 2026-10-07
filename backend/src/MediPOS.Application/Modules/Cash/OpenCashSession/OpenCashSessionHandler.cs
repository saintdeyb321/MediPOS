using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.AuditSupport;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Cash;

namespace MediPOS.Application.Modules.Cash.OpenCashSession;

public sealed record OpenCashSessionCommand(Guid TenantId, Guid BranchId, decimal OpeningAmount);

// One atomic persistence operation: neither the session nor its audit may survive alone.
public interface IOpenCashSessionWriter
{
    Task SaveAsync(CashSession session, AuditLog audit, CancellationToken cancellationToken);
}

public sealed class OpenCashSessionHandler(
    ResolveAccessContextHandler accessResolver, IOpenCashSessionWriter writer, TimeProvider timeProvider)
{
    public async Task<OpenCashSessionDetails> HandleAsync(OpenCashSessionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.TenantId == Guid.Empty || command.BranchId == Guid.Empty)
            throw new ApplicationErrorException(ApplicationErrors.InvalidRequest);
        var now = timeProvider.GetUtcNow();
        var access = await CashOperationalAccess.ResolveAsync(accessResolver, command.TenantId, command.BranchId,
            now, cancellationToken).ConfigureAwait(false);
        if (!CashSession.IsValidOpeningAmount(command.OpeningAmount))
            throw new ApplicationErrorException(CashSessionErrors.InvalidOpeningAmount);
        var session = CashSession.Open(access.TenantId, command.BranchId, access.MembershipId,
            command.OpeningAmount, now, access.UserId);
        var audit = AuditTrail.Record(session.TenantId, access.UserId, AuditAction.CashSessionOpened, session.Id,
            session.OpenedAt, null, JsonSerializer.Serialize(new
            {
                branchId = session.BranchId,
                membershipId = session.MembershipId,
                openingAmount = session.OpeningAmount,
                openedAt = session.OpenedAt,
            }));
        await writer.SaveAsync(session, audit, cancellationToken).ConfigureAwait(false);
        return OpenCashSessionDetails.From(session);
    }
}
