namespace MediPOS.Domain.Modules.ElectronicDocuments;

public sealed class ElectronicDocumentSeries
{
    private ElectronicDocumentSeries() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid LegalEntityId { get; private set; }
    public Guid AuthorizedByMembershipId { get; private set; }
    public string DocumentType { get; private set; } = "";
    public string Code { get; private set; } = "";
    public string Profile { get; private set; } = "";
    public string IssuerAddress { get; private set; } = "";
    public bool IsActive { get; private set; }
    public long NextNumber { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public uint Version { get; private set; }
    public static ElectronicDocumentSeries Create(Guid tenant, Guid legal, Guid membership, string type,
        string code, string profile, string issuerAddress, long firstNumber, DateTimeOffset now)
    {
        ElectronicDocumentRules.Id(tenant); ElectronicDocumentRules.Id(legal); ElectronicDocumentRules.Id(membership);
        ElectronicDocumentTypes.Validate(type); ElectronicDocumentRules.Series(profile, code);
        ElectronicDocumentRules.Text(issuerAddress, 512, "issuer_address_required"); ElectronicDocumentRules.Utc(now);
        ElectronicDocumentRules.Require(firstNumber is >= 1 and <= 99999999, "number_invalid");
        return new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant,
            LegalEntityId = legal,
            AuthorizedByMembershipId = membership,
            DocumentType = type,
            Code = code,
            Profile = profile,
            IssuerAddress = issuerAddress,
            IsActive = true,
            NextNumber = firstNumber,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }
    public long Allocate(DateTimeOffset now)
    {
        ElectronicDocumentRules.Utc(now);
        ElectronicDocumentRules.Require(now >= UpdatedAtUtc, "time_invalid");
        ElectronicDocumentRules.Require(IsActive && NextNumber <= 99999999, "series_unavailable");
        UpdatedAtUtc = now; return NextNumber++;
    }
    public void SetActive(bool active, Guid membership, DateTimeOffset now)
    {
        ElectronicDocumentRules.Id(membership); ElectronicDocumentRules.Utc(now);
        ElectronicDocumentRules.Require(now >= UpdatedAtUtc, "time_invalid");
        IsActive = active; AuthorizedByMembershipId = membership; UpdatedAtUtc = now;
    }
}
