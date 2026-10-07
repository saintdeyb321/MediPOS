using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog.ProductImport;
using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Catalog.ProductImport;
using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.UnitTests.Modules.Catalog;

public sealed class ProductImportTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 14, 0, 0, TimeSpan.Zero);
    private static readonly Guid Category = Guid.NewGuid();
    private static readonly int[] MixedRowNumbers = [4, 5, 6];
    private static readonly int[] FailedRowNumbers = [4, 6];

    [Fact]
    public async Task MixedRowsReuseDomainCreateProductAndBaseUnitTogetherAndProduceOnlyOneMinimalAudit()
    {
        var setup = new Setup(
            Row(4), Row(5) with { ProductType = "medicine", ActiveIngredients = "Paracetamol | Cafeína", Strengths = "500 mg | 30 mg", DosageForm = "Tableta", Route = "Oral" },
            Row(6) with { RetailPrice = "-1" });
        using var input = new MemoryStream([1]);
        var result = await setup.Handler.HandleAsync(new(setup.TenantId, @"C:\fakepath\items.xlsx", input, setup.ActorId), TestContext.Current.CancellationToken);
        Assert.Equal("completed", result.Status);
        Assert.Equal("items.xlsx", result.FileName);
        Assert.Equal(3, result.TotalRows);
        Assert.Equal(2, result.ValidRows);
        Assert.Equal(2, result.ImportedRows);
        Assert.Equal(1, result.InvalidRows);
        Assert.Equal(MixedRowNumbers, result.Rows.Select(value => value.RowNumber));
        Assert.Equal("import.invalid_price", result.Rows[2].ErrorCode);
        Assert.Null(result.Rows[2].BusinessProductId);
        Assert.Equal(2, setup.Store.Writes.Count);
        Assert.All(setup.Store.Writes, value =>
        {
            Assert.Equal(setup.TenantId, value.Product.TenantId);
            Assert.Null(value.Product.GlobalProductId);
            Assert.Equal(value.Product.Id, value.Unit.BusinessProductId);
            Assert.Equal(setup.TenantId, value.Unit.TenantId);
            Assert.Equal(1m, value.Unit.ConversionToBase);
            Assert.True(value.Unit.IsBaseUnit && value.Unit.IsActive);
            Assert.Equal("Unidad", value.Unit.Name);
        });
        var medicine = setup.Store.Writes.Single(value => value.Product.ProductType == ProductType.Medicine).Product.Medicine!;
        Assert.Contains(medicine.Components, value => value.Ingredient == "PARACETAMOL" && value.StrengthNormalized == "500 MG");
        Assert.Contains(medicine.Components, value => value.Ingredient == "CAFEINA" && value.StrengthNormalized == "30 MG");
        Assert.Matches("^[0-9a-f]{64}$", medicine.EquivalenceKey!);
        var audit = Assert.Single(setup.Store.Audits);
        Assert.Equal(AuditAction.CatalogProductsImported, audit.Action);
        Assert.Equal(AuditEntityType.ImportJob, audit.EntityType);
        Assert.Equal(result.ImportJobId, audit.EntityId);
        Assert.Equal(setup.TenantId, audit.TenantId);
        Assert.Equal(setup.ActorId, audit.ActorId);
        using var snapshot = JsonDocument.Parse(audit.AfterJson!);
        Assert.Equal(3, snapshot.RootElement.EnumerateObject().Count());
        Assert.Equal(3, snapshot.RootElement.GetProperty("totalRows").GetInt32());
        Assert.Equal(2, snapshot.RootElement.GetProperty("importedRows").GetInt32());
        Assert.Equal(1, snapshot.RootElement.GetProperty("invalidRows").GetInt32());
        Assert.True(input.CanRead);
    }

    [Theory]
    [InlineData("1.23456")]
    [InlineData("1e-30")]
    [InlineData("1.00000000000000000000000000001")]
    [InlineData("100000000000000")]
    [InlineData("1,25")]
    [InlineData("NaN")]
    public async Task PriceIsNotRoundedAndInvalidRowDoesNotBlockAnother(string price)
    {
        var setup = new Setup(Row(4) with { RetailPrice = price }, Row(5));
        var result = await RunAsync(setup);
        Assert.Equal(1, result.ImportedRows);
        Assert.Equal("import.invalid_price", result.Rows[0].ErrorCode);
        Assert.Single(setup.Store.Writes);
    }

    [Theory]
    [InlineData("1.2500", "2.5E+0")]
    [InlineData("99999999999999.9999", "0")]
    [InlineData("0.0001", "")]
    public async Task ExactCatalogPricesPersistIncludingMaximumAndExponent(string retail, string wholesale)
    {
        var setup = new Setup(Row(4) with { RetailPrice = retail, WholesalePrice = wholesale });
        Assert.Equal(1, (await RunAsync(setup)).ImportedRows);
    }

    [Theory]
    [InlineData("code", "import.missing_internal_code")]
    [InlineData("type", "import.invalid_product_type")]
    [InlineData("category", "import.category_not_found")]
    [InlineData("name", "import.invalid_name")]
    [InlineData("brand", "import.invalid_brand")]
    [InlineData("unit", "import.invalid_base_unit")]
    [InlineData("active", "import.invalid_is_active")]
    [InlineData("retail_medicine", "import.retail_medicine_fields")]
    [InlineData("components", "import.medicine_components_mismatch")]
    [InlineData("medicine", "import.invalid_medicine")]
    public async Task InvalidFieldsHaveStableErrorsAndNeverCreateProducts(string field, string code)
    {
        var row = field switch
        {
            "code" => Row(4) with { InternalCode = "" },
            "type" => Row(4) with { ProductType = "service" },
            "category" => Row(4) with { CategoryId = Guid.NewGuid().ToString("D") },
            "name" => Row(4) with { Name = "" },
            "brand" => Row(4) with { BrandOrLaboratory = "" },
            "unit" => Row(4) with { BaseUnitName = "" },
            "active" => Row(4) with { IsActive = "yes" },
            "retail_medicine" => Row(4) with { Route = "Oral" },
            "components" => Row(4) with { ProductType = "medicine", ActiveIngredients = "A | B", Strengths = "1 mg", DosageForm = "Tableta" },
            _ => Row(4) with { ProductType = "medicine", ActiveIngredients = "A", Strengths = "1 mg", DosageForm = "" },
        };
        var setup = new Setup(row);
        var result = await RunAsync(setup);
        Assert.Equal("completed", result.Status);
        Assert.Equal(0, result.ImportedRows);
        Assert.Equal(1, result.InvalidRows);
        Assert.Equal(code, Assert.Single(result.Rows).ErrorCode);
        Assert.Empty(setup.Store.Writes);
    }

    [Fact]
    public async Task DomainLengthInvariantsAreReusedWithoutChangingExistingRules()
    {
        var setup = new Setup(Row(4) with { Name = new string('n', 257) },
            Row(5) with { InternalCode = new string('c', 65) }, Row(6) with { Barcode = new string('b', 129) },
            Row(7) with { BrandOrLaboratory = new string('l', 201) }, Row(8) with { BaseUnitName = new string('u', 129) });
        var result = await RunAsync(setup);
        Assert.Equal(5, result.InvalidRows);
        Assert.Empty(setup.Store.Writes);
    }

    [Fact]
    public async Task AllRepeatedCodesAreInvalidAndExistingProductsAreNeverOverwritten()
    {
        var setup = new Setup(Row(4) with { InternalCode = " repeated " }, Row(5) with { InternalCode = "repeated" },
            Row(6) with { InternalCode = "existing" }, Row(7));
        setup.Store.Existing.Add("existing");
        var result = await RunAsync(setup);
        Assert.Equal(1, result.ImportedRows);
        Assert.Equal(3, result.InvalidRows);
        Assert.All(result.Rows.Take(3), value => Assert.Equal("import.duplicate_internal_code", value.ErrorCode));
        Assert.Single(setup.Store.Writes);
    }

    [Fact]
    public async Task DatabaseConflictOutcomeIsCountedInvalidAndOtherValidRowsStillCommit()
    {
        var setup = new Setup(Row(4), Row(5));
        setup.Store.ConflictRow = 4;
        var result = await RunAsync(setup);
        Assert.Equal(1, result.ImportedRows);
        Assert.Equal(1, result.InvalidRows);
        Assert.Equal("import.duplicate_internal_code", result.Rows[0].ErrorCode);
        Assert.Equal(1, result.ValidRows);
    }

    [Theory]
    [InlineData("missing", "license.not_found")]
    [InlineData("suspended", "license.operation_denied")]
    [InlineData("foreign", "tenant.scope_conflict")]
    public async Task InvalidLicenseRejectsBeforeReadingOrCreatingAJob(string kind, string code)
    {
        var setup = new Setup(Row(4));
        setup.Store.License = kind switch
        {
            "missing" => null,
            "foreign" => License.Create(Guid.NewGuid(), Now.AddDays(-1), Now.AddDays(1), 3, LicenseStatus.Active, setup.ActorId, Now),
            _ => License.Create(setup.TenantId, Now.AddDays(-1), Now.AddDays(1), 3, LicenseStatus.Suspended, setup.ActorId, Now),
        };
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => RunAsync(setup));
        Assert.Equal(code, error.Error.Code);
        Assert.Equal(0, setup.Reader.Reads);
        Assert.Null(setup.Store.Job);
    }

    [Fact]
    public async Task UnsupportedWorkbookVersionAndReaderErrorsNeverStartAJob()
    {
        var setup = new Setup(Row(4));
        setup.Reader.Version = "2";
        Assert.Equal(ProductImportErrors.TemplateVersion, (await Assert.ThrowsAsync<ApplicationErrorException>(() => RunAsync(setup))).Error);
        Assert.Null(setup.Store.Job);
        setup.Reader.Version = "1";
        setup.Reader.Error = ProductImportErrors.InvalidFile;
        Assert.Equal(ProductImportErrors.InvalidFile, (await Assert.ThrowsAsync<ApplicationErrorException>(() => RunAsync(setup))).Error);
        Assert.Null(setup.Store.Job);
    }

    [Fact]
    public async Task InfrastructureFailureKeepsCommittedOutcomesAndMarksJobFailed()
    {
        var setup = new Setup(Row(4), Row(5), Row(6) with { RetailPrice = "-1" });
        setup.Store.ThrowRow = 5;
        var result = await RunAsync(setup);
        Assert.Equal("failed", result.Status);
        Assert.Equal(3, result.TotalRows);
        Assert.Equal(1, result.ImportedRows);
        Assert.Equal(1, result.InvalidRows);
        Assert.Equal(FailedRowNumbers, result.Rows.Select(value => value.RowNumber));
        Assert.Empty(setup.Store.Audits);
        Assert.DoesNotContain(result.Rows, value => value.ErrorMessage?.Contains("internal", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task InitialResultIsBoundedAndLaterPagesAreOrdered()
    {
        var setup = new Setup(Enumerable.Range(4, 205).Select(Row).ToArray());
        var initial = await RunAsync(setup);
        Assert.Equal(205, initial.ImportedRows);
        Assert.Equal(100, initial.Rows.Count);
        Assert.Equal(103, initial.NextAfterRowNumber);
        var next = await new GetImportJobResultHandler(setup.Store).HandleAsync(new(setup.TenantId, initial.ImportJobId, 103, 200),
            TestContext.Current.CancellationToken);
        Assert.Equal(105, next.Rows.Count);
        Assert.Equal(104, next.Rows[0].RowNumber);
        Assert.Null(next.NextAfterRowNumber);
        Assert.DoesNotContain(typeof(ProductImportRow).GetProperties(), property =>
            property.Name is "TenantId" or "BusinessProductId" or "GlobalProductId" or "Stock" or "Cost" or "InventoryLot");
    }

    [Fact]
    public void ImportStatusesAndHistoryUseStableCodesAndUtcAndCannotFinishTwice()
    {
        foreach (var status in Enum.GetValues<ImportJobStatus>()) Assert.Equal(status, ProductImportCodes.JobFromCode(ProductImportCodes.ToCode(status)));
        foreach (var status in Enum.GetValues<ImportRowStatus>()) Assert.Equal(status, ProductImportCodes.RowFromCode(ProductImportCodes.ToCode(status)));
        var job = ImportJob.Create(Guid.NewGuid(), Guid.NewGuid(), "items.xlsx", "1", 3, Now.ToOffset(TimeSpan.FromHours(-5)));
        Assert.Equal(7, job.Id.Version);
        Assert.Equal(TimeSpan.Zero, job.CreatedAt.Offset);
        Assert.Throws<ArgumentOutOfRangeException>(() => job.Complete(1, 1, Now));
        job.Complete(2, 1, Now);
        Assert.Equal(2, job.ValidRows);
        Assert.Throws<InvalidOperationException>(() => job.Fail(2, 1, Now));
        Assert.Throws<ArgumentException>(() => ImportRowResult.Invalid(job, 4, "import.invalid", "Invalid"));
    }

    private static async Task<ImportJobResult> RunAsync(Setup setup)
    {
        using var input = new MemoryStream([1]);
        return await setup.Handler.HandleAsync(new(setup.TenantId, "items.xlsx", input, setup.ActorId), TestContext.Current.CancellationToken);
    }
    private static ProductImportRow Row(int number) => new(number, "CODE-" + number, "retail", "Producto", Category.ToString("D"),
        "Marca", "1.2500", "Unidad", "001234", "", "", "", "", "", "", "");
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Setup
    {
        internal Guid TenantId { get; } = Guid.NewGuid();
        internal Guid ActorId { get; } = Guid.NewGuid();
        internal Reader Reader { get; }
        internal Store Store { get; }
        internal ImportProductsFromExcelHandler Handler { get; }
        internal Setup(params ProductImportRow[] rows)
        {
            Reader = new(rows);
            Store = new(TenantId, ActorId);
            Handler = new(Reader, Store, new Clock());
        }
    }
    private sealed class Reader(ProductImportRow[] rows) : IProductImportWorkbookReader
    {
        internal string Version { get; set; } = "1";
        internal ApplicationError? Error { get; set; }
        internal int Reads { get; private set; }
        public Task<ProductImportWorkbook> ReadAsync(Stream stream, string fileName, CancellationToken cancellationToken)
        {
            Reads++;
            if (Error is not null) throw new ApplicationErrorException(Error);
            return Task.FromResult(new ProductImportWorkbook(Version, rows));
        }
    }
    private sealed class Store(Guid tenant, Guid actor) : IProductImportStore
    {
        internal License? License { get; set; } = License.Create(tenant, Now.AddDays(-1), Now.AddMonths(1), 3, LicenseStatus.Active, actor, Now);
        internal HashSet<string> Existing { get; } = new(StringComparer.Ordinal);
        internal ImportJob? Job { get; private set; }
        internal List<ImportRowResult> Rows { get; } = [];
        internal List<(BusinessProduct Product, ProductUnit Unit)> Writes { get; } = [];
        internal List<AuditLog> Audits { get; } = [];
        internal int? ConflictRow { get; set; }
        internal int? ThrowRow { get; set; }
        public Task<License?> FindLicenseAsync(Guid tenantId, CancellationToken cancellationToken) => Task.FromResult(License);
        public Task<IReadOnlySet<Guid>> FindActiveCategoriesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid> { Category });
        public Task<IReadOnlySet<string>> FindExistingCodesAsync(Guid tenantId, IReadOnlyCollection<string> codes, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(Existing);
        public Task StartAsync(ImportJob job, CancellationToken cancellationToken) { Job = job; return Task.CompletedTask; }
        public Task SaveInvalidRowsAsync(ImportJob job, IReadOnlyList<ImportRowResult> rows, CancellationToken cancellationToken)
        { Rows.AddRange(rows); return Task.CompletedTask; }
        public Task<ImportRowResult> ImportRowAsync(ImportJob job, int rowNumber, BusinessProduct product, ProductUnit baseUnit, CancellationToken cancellationToken)
        {
            if (rowNumber == ThrowRow) throw new IOException("internal infrastructure failure");
            var result = rowNumber == ConflictRow
                ? ImportRowResult.Invalid(job, rowNumber, ProductImportErrors.DuplicateCode.Code, ProductImportErrors.DuplicateCode.Message)
                : ImportRowResult.Imported(job, rowNumber, product);
            if (result.Status == ImportRowStatus.Imported) Writes.Add((product, baseUnit));
            Rows.Add(result);
            return Task.FromResult(result);
        }
        public Task CompleteAsync(ImportJob job, AuditLog summary, CancellationToken cancellationToken)
        { Audits.Add(summary); return Task.CompletedTask; }
        public Task FailAsync(Guid tenantId, Guid jobId, DateTimeOffset at, CancellationToken cancellationToken)
        {
            Job!.Fail(Rows.Count(value => value.Status == ImportRowStatus.Imported), Rows.Count(value => value.Status == ImportRowStatus.Invalid), at);
            return Task.CompletedTask;
        }
        public Task<ImportJobPage?> FindAsync(Guid tenantId, Guid jobId, int afterRowNumber, int limit, CancellationToken cancellationToken)
        {
            var rows = Rows.Where(value => value.RowNumber > afterRowNumber).OrderBy(value => value.RowNumber).ToArray();
            return Task.FromResult<ImportJobPage?>(new(Job!, rows.Take(limit).ToArray(), rows.Length > limit));
        }
    }
}
