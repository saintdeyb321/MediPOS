using MediPOS.Domain.Modules.Catalog;

namespace MediPOS.Domain.Modules.Purchasing;

public enum PurchaseStatus { Draft, Confirmed }

public static class PurchaseStatusCodes
{
    public static string ToCode(PurchaseStatus status) => status switch
    {
        PurchaseStatus.Draft => "draft",
        PurchaseStatus.Confirmed => "confirmed",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
    public static PurchaseStatus FromCode(string code) => code switch
    {
        "draft" => PurchaseStatus.Draft,
        "confirmed" => PurchaseStatus.Confirmed,
        _ => throw new InvalidOperationException("Unknown persisted purchase status."),
    };
}

public sealed class Purchase
{
    private Purchase() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid? SupplierId { get; private set; }
    public string? DocumentReference { get; private set; }
    public PurchaseStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public Guid CreatedByActorId { get; private set; }
    public Guid? ConfirmedByActorId { get; private set; }

    public static Purchase Create(Guid tenantId, Guid branchId, Guid? supplierId, string? reference,
        Guid actorId, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty || branchId == Guid.Empty || actorId == Guid.Empty || supplierId == Guid.Empty)
            throw new ArgumentException("Valid tenant, branch, optional supplier and actor identifiers are required.");
        return new Purchase
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            BranchId = branchId,
            SupplierId = supplierId,
            DocumentReference = PurchasingFields.Optional(reference, 256),
            Status = PurchaseStatus.Draft,
            CreatedByActorId = actorId,
            CreatedAt = now.ToUniversalTime(),
        };
    }

    public void EnsureDraft()
    {
        if (Status != PurchaseStatus.Draft) throw new InvalidOperationException("Confirmed purchases are terminal.");
    }

    public void ValidateConfirmation(IReadOnlyList<PurchaseLine> lines, IReadOnlyDictionary<Guid, BusinessProduct> products)
    {
        EnsureDraft();
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(products);
        if (lines.Count == 0) throw new ArgumentException("A purchase needs at least one line.");
        if (lines.Select(value => value.Id).Distinct().Count() != lines.Count)
            throw new ArgumentException("Purchase lines must be distinct.");
        foreach (var line in lines)
        {
            if (!products.TryGetValue(line.BusinessProductId, out var product))
                throw new ArgumentException("Every line requires a product.");
            line.ValidateForConfirmation(this, product);
        }
    }

    public void Confirm(IReadOnlyList<PurchaseLine> lines, IReadOnlyDictionary<Guid, BusinessProduct> products,
        Guid actorId, DateTimeOffset now)
    {
        if (actorId == Guid.Empty) throw new ArgumentException("Actor is required.", nameof(actorId));
        ValidateConfirmation(lines, products);
        Status = PurchaseStatus.Confirmed;
        ConfirmedByActorId = actorId;
        ConfirmedAt = now.ToUniversalTime();
    }
}
