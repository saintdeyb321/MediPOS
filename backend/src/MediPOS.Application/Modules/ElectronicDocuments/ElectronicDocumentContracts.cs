using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Domain.Modules.Branches;
using MediPOS.Domain.Modules.ElectronicDocuments;
using MediPOS.Domain.Modules.SalesPos;

namespace MediPOS.Application.Modules.ElectronicDocuments;

public sealed record FiscalCustomer(string IdentityType, string IdentityNumber, string Name, string Address);
public sealed record FiscalLine(Guid SaleLineId, decimal NetAmount, decimal TaxAmount, string TaxClassification);
public sealed record FiscalInput(string Profile, string Currency, FiscalCustomer Customer, IReadOnlyList<FiscalLine> Lines);
public sealed record ConfigureElectronicSeriesCommand(Guid TenantId, Guid BranchId, string Type, string Code,
    string Profile, string IssuerAddress, long FirstNumber);
public sealed record RequestElectronicDocumentCommand(Guid TenantId, Guid BranchId, Guid SaleId, Guid SeriesId, string Type, FiscalInput Fiscal);
public sealed record SetElectronicSeriesActiveCommand(Guid TenantId, Guid BranchId, Guid SeriesId, bool IsActive, uint ExpectedVersion);
public sealed record ReviewElectronicDocumentCommand(Guid TenantId, Guid BranchId, Guid DocumentId, bool Reschedule, uint ExpectedVersion);
public sealed record ElectronicRequestData(Sale Sale, Branch Branch, LegalEntity Legal, ElectronicDocumentSeries Series, ElectronicDocument? Existing);
public sealed record ElectronicDocumentFilter(Guid TenantId, Guid BranchId, Guid? Id = null, Guid? SaleId = null,
    DateTimeOffset? FromUtc = null, DateTimeOffset? ToUtc = null, string? Type = null, string? Status = null,
    bool NeedsAttention = false, int Offset = 0, int Limit = 50);
public sealed record ElectronicDocumentDetails(Guid Id, Guid SaleId, Guid BranchId, string Type, string Number,
    string Status, string Currency, decimal Total, DateTimeOffset CreatedAtUtc, DateTimeOffset? SubmittedAtUtc,
    string? ProviderReference, string? ErrorCode, uint Version, bool CommercialVoidNeedsReview);
public sealed record ElectronicDocumentPage(IReadOnlyList<ElectronicDocumentDetails> Items, bool HasMore);
public sealed record ElectronicAttemptDetails(int Number, string Phase, DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc, string Outcome, string? ErrorCode, string? ProviderReference);
public sealed record ElectronicAttemptPage(IReadOnlyList<ElectronicAttemptDetails> Items, bool HasMore);
public sealed record ElectronicSeriesDetails(Guid Id, Guid LegalEntityId, string Type, string Code, string Profile,
    bool IsActive, long NextNumber, uint Version);
public sealed record ElectronicSeriesPage(IReadOnlyList<ElectronicSeriesDetails> Items, bool HasMore);

public interface IElectronicDocumentStore
{
    Task<LegalEntity?> IssuerAsync(Guid tenant, Guid branch, CancellationToken token);
    Task<ElectronicDocumentSeries?> LockSeriesAsync(Guid tenant, Guid branch, Guid id, CancellationToken token);
    Task SaveSeriesAsync(ElectronicDocumentSeries series, Guid actor, CancellationToken token);
    Task<ElectronicSeriesPage> ReadSeriesAsync(Guid tenant, Guid branch, int offset, int limit, CancellationToken token);
    Task<ElectronicRequestData?> LoadRequestAsync(RequestElectronicDocumentCommand command, CancellationToken token);
    Task SaveRequestAsync(ElectronicDocument document, Guid actor, CancellationToken token);
    Task<ElectronicDocument?> LockDocumentAsync(Guid tenant, Guid branch, Guid id, CancellationToken token);
    Task SaveReviewAsync(ElectronicDocument document, Guid actor, CancellationToken token);
    Task<ElectronicDocumentPage> ReadAsync(ElectronicDocumentFilter filter, AccessContext access, CancellationToken token);
    Task<ElectronicAttemptPage> AttemptsAsync(Guid tenant, Guid branch, Guid document, AccessContext access, int offset, int limit, CancellationToken token);
}
