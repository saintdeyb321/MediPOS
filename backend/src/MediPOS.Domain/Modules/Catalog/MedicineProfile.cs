namespace MediPOS.Domain.Modules.Catalog;

// A global product's dependent profile; its primary key is also the global product reference.
public sealed class MedicineProfile
{
    private MedicineProfile() { }
    public Guid GlobalProductId { get; private set; }
    public ProductType ProductType { get; private set; } = ProductType.Medicine;
    public MedicineData Data { get; private set; } = null!;

    internal static MedicineProfile Create(Guid globalProductId, MedicineData medicine) => new()
    {
        GlobalProductId = globalProductId,
        Data = medicine.Copy(),
    };
}
