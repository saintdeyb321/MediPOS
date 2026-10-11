using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog.OfficialImport;
using MediPOS.Domain.Modules.Catalog.OfficialImport;
using MediPOS.Infrastructure.Modules.Catalog.OfficialImport;
using static MediPOS.UnitTests.Modules.Catalog.OfficialImport.OfficialCatalogTestSetup;

namespace MediPOS.UnitTests.Modules.Catalog.OfficialImport;

public sealed class OfficialCatalogImportTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    [Fact]
    public async Task CsvPreservesOriginalUnicodeWhitespaceQuotesCommasNewlinesIdentifiersAndStrengthWithoutPublication()
    {
        var setup = new OfficialCatalogTestSetup(); var fields = Fields("000123-A", "  Medicamento, \"original\"\r\nsegunda línea  ");
        var csv = Csv(fields); var result = await setup.Run(csv);
        Assert.Equal("staged", result.Status); Assert.Equal(1, result.ValidRows); Assert.Equal(1, result.TotalRows);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(csv))), result.SourceChecksumSha256);
        var row = Assert.Single(setup.Store.Rows); Assert.Equal(fields["Name"], row.OriginalName); Assert.Equal(fields["Strength"], row.OriginalStrengthText);
        Assert.Equal("000123-A", row.SourceRecordId); Assert.Equal(fields, JsonSerializer.Deserialize<Dictionary<string, string>>(row.RawFieldsJson)!);
        Assert.Equal(7, result.Id.Version); Assert.Equal(setup.Auth.Actor, result.StartedByActorId);
        Assert.DoesNotContain("GlobalProduct", JsonSerializer.Serialize(result), StringComparison.Ordinal); Assert.DoesNotContain("TenantId", JsonSerializer.Serialize(row), StringComparison.Ordinal);
    }
    [Theory]
    [InlineData("empty", "file_empty")]
    [InlineData("headers", "headers_invalid")]
    [InlineData("quote", "csv_invalid")]
    [InlineData("columns", "columns_invalid")]
    [InlineData("nul", "file_control_character")]
    public async Task StructurallyInvalidFilesHaveAStableFailedDiagnosticAndNeverBecomeStaged(string kind, string code)
    {
        var setup = new OfficialCatalogTestSetup(); var csv = kind switch
        { "empty" => "", "headers" => "Name\r\nProducto", "quote" => Header + "\n\"unclosed", "columns" => Header + "\nx", _ => Csv(Fields(name: "A\0B")) };
        var result = await setup.Run(csv); Assert.Equal("failed", result.Status); Assert.Equal("official_catalog." + code, result.FailureCode);
        Assert.Empty(setup.Store.Rows); Assert.Single(result.Failures);
        Assert.Equal(result.Id, (await setup.Run(csv)).Id); Assert.Equal(1, Assert.Single(setup.Store.Batches).Value.AttemptCount);
    }
    [Fact]
    public async Task InvalidUtf8IsRejectedEvenWhenTheExtensionSaysCsv()
    {
        var setup = new OfficialCatalogTestSetup(); using var stream = new MemoryStream([0xff, 0xfe, 0x41, 0x00]);
        var result = await setup.Handler.HandleAsync(new(Source.Code, "v1", null, "wrong.csv", OfficialCatalogInput.Format, stream), Token);
        Assert.Equal("official_catalog.utf8_invalid", result.FailureCode); Assert.Equal("failed", result.Status);
    }
    [Theory]
    [InlineData("SourceRecordId", "", "record_id_missing")]
    [InlineData("Name", "   ", "name_missing")]
    [InlineData("Ingredients", "", "pharmaceutical_data_incomplete")]
    [InlineData("Strength", "500", "strength_ambiguous")]
    [InlineData("Strength", "entre 200 y 500 mg", "strength_ambiguous")]
    [InlineData("Presentation", "Frasco ? mL", "presentation_ambiguous")]
    [InlineData("Route", "Oral\u0001", "control_character")]
    public async Task InvalidRowsKeepTheirSourceAndCodesWithoutInventingClinicalInformation(string field, string value, string code)
    {
        var setup = new OfficialCatalogTestSetup(); var fields = Fields(); fields[field] = value;
        var result = await setup.Run(Csv(fields)); Assert.Equal("staged", result.Status); Assert.Equal(1, result.InvalidRows); Assert.Equal(0, result.ValidRows);
        var row = Assert.Single(setup.Store.Rows); Assert.Contains("official_catalog." + code, row.ValidationErrorCodes);
        Assert.Equal(value, JsonSerializer.Deserialize<Dictionary<string, string>>(row.RawFieldsJson)![field]);
    }
    [Fact]
    public async Task TooLongFieldsArePreservedAsInvalidWithinTheHardStorageEnvelope()
    {
        var setup = new OfficialCatalogTestSetup(); var name = new string('a', 513);
        var result = await setup.Run(Csv(Fields(name: name))); Assert.Equal(1, result.InvalidRows);
        Assert.Equal(name, Assert.Single(setup.Store.Rows).OriginalName); Assert.Contains("official_catalog.field_too_long", setup.Store.Rows[0].ValidationErrorCodes);
        var failed = await setup.Run(Csv(Fields(name: new string('a', 8193))), "v2"); Assert.Equal("official_catalog.cell_envelope_exceeded", failed.FailureCode);
    }
    [Fact]
    public async Task ExactIdAndPotentialDuplicatesRemainSeparateRowsAcrossWritePages()
    {
        var setup = new OfficialCatalogTestSetup(); var first = Fields("001", "Marca"); var sameId = Fields("001", "Distinta");
        var variation = Fields("002", " marca "); var otherPresentation = Fields("003", "Marca"); otherPresentation["Presentation"] = "Caja 30 tabletas";
        var result = await setup.Run(Csv(first, new(first, StringComparer.Ordinal), sameId, variation, otherPresentation));
        Assert.Equal(5, result.TotalRows); Assert.Equal(3, result.DuplicateRows); Assert.Equal(3, result.ValidRows); Assert.Equal(2, result.InvalidRows);
        Assert.Equal(5, setup.Store.Rows.Count); Assert.Equal(3, setup.Store.AppendCalls);
        Assert.Contains("exact_row", setup.Store.Rows[1].DuplicateSignals); Assert.Contains("source_id", setup.Store.Rows[2].DuplicateSignals);
        Assert.Equal(["potential"], setup.Store.Rows[3].DuplicateSignals); Assert.Empty(setup.Store.Rows[4].DuplicateSignals);
    }
    [Fact]
    public async Task SameContentIsIdempotentButAnotherChecksumOfTheVersionConflicts()
    {
        var setup = new OfficialCatalogTestSetup(); var csv = Csv(Fields()); var first = await setup.Run(csv); var second = await setup.Run(csv);
        Assert.Equal(first.Id, second.Id); Assert.Single(setup.Store.Rows); Assert.Equal(1, setup.Store.AppendCalls);
        Assert.Equal("official_catalog.source_version_conflict", (await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Run(Csv(Fields(name: "Otro"))))).Error.Code);
    }
    [Fact]
    public async Task PartialFailureResumesWithoutReplacingCommittedRowsOrLosingDiagnostics()
    {
        var setup = new OfficialCatalogTestSetup(); setup.Store.ThrowOnAppend = 2; var csv = Csv(Fields("1"), Fields("2"), Fields("3"), Fields("4"), Fields("5"));
        var failed = await setup.Run(csv); Assert.Equal("failed", failed.Status); Assert.Equal(2, failed.TotalRows);
        var originalIds = setup.Store.Rows.Select(row => row.Id).ToArray(); setup.Store.ThrowOnAppend = 0;
        var retried = await setup.Run(csv); Assert.Equal("staged", retried.Status); Assert.Equal(failed.Id, retried.Id); Assert.Equal(5, retried.TotalRows); Assert.Equal(2, retried.AttemptCount);
        Assert.Equal(originalIds, setup.Store.Rows.Take(2).Select(row => row.Id)); Assert.Single(retried.Failures); Assert.Equal("official_catalog.file_unreadable", retried.Failures[0].Code);
    }
    [Fact]
    public async Task CancellationKeepsThePersistedPrefixAndCanResumeWithTheSameChecksum()
    {
        var setup = new OfficialCatalogTestSetup(); using var cancellation = new CancellationTokenSource(); setup.Store.AfterAppend = cancellation.Cancel;
        var csv = Csv(Fields("1"), Fields("2"), Fields("3"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => setup.Run(csv, token: cancellation.Token));
        Assert.Equal("official_catalog.processing_cancelled", Assert.Single(setup.Store.Batches).Value.FailureCode); Assert.Equal(2, setup.Store.Rows.Count);
        setup.Store.AfterAppend = null; Assert.Equal(3, (await setup.Run(csv)).TotalRows);
    }
    [Fact]
    public async Task SizeRowsAndSchemaLimitsRejectControlledInputs()
    {
        var setup = new OfficialCatalogTestSetup { Options = new() { MaximumFileBytes = 1 } };
        Assert.Equal("official_catalog.file_too_large", (await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Run(Csv(Fields())))).Error.Code); Assert.Empty(setup.Store.Batches);
        setup.Options = new() { MaximumRows = 1, WritePageSize = 1 };
        Assert.Equal("official_catalog.rows_exceeded", (await setup.Run(Csv(Fields("1"), Fields("2")))).FailureCode);
        using var input = new MemoryStream(); Assert.Equal("official_catalog.schema_unsupported", (await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(new(Source.Code, "x", null, "a.xlsx", "xlsx", input), Token))).Error.Code);
    }
    [Theory]
    [InlineData("Owner")]
    [InlineData("Cashier")]
    [InlineData("Pharmacist")]
    [InlineData("anonymous")]
    public async Task TenantRoleOrAnonymousIdentityDoesNotSupplyPlatformAuthority(string role)
    {
        Assert.False(string.IsNullOrEmpty(role)); var setup = new OfficialCatalogTestSetup(); setup.Auth.Allowed = false;
        Assert.Equal("official_catalog.platform_denied", (await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Run(Csv(Fields())))).Error.Code);
        var get = new GetOfficialCatalogImportBatchHandler(setup.Auth, setup.Store);
        await Assert.ThrowsAsync<ApplicationErrorException>(() => get.HandleAsync(new(Guid.NewGuid()), Token)); Assert.Empty(setup.Store.Batches);
    }
    [Fact]
    public async Task UnapprovedSourceCannotStartAnImportAndRevocationStopsFurtherPages()
    {
        var setup = new OfficialCatalogTestSetup(); setup.Sources.Allowed = false;
        Assert.Equal("official_catalog.source_not_authorized", (await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Run(Csv(Fields())))).Error.Code);
        setup.Sources.Allowed = true; setup.Store.AfterAppend = () => setup.Auth.Allowed = false;
        await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Run(Csv(Fields("1"), Fields("2"), Fields("3"))));
        Assert.Equal("failed", Assert.Single(setup.Store.Batches).Value.Status); Assert.Equal(2, setup.Store.Rows.Count);
    }
    [Fact]
    public async Task AdministrativePagingFiltersAndSummariesAreBoundedAndTraceable()
    {
        var setup = new OfficialCatalogTestSetup(); var invalid = Fields("2"); invalid["Strength"] = "500";
        var batch = await setup.Run(Csv(Fields("1"), invalid, Fields("3"))); var handler = new GetOfficialCatalogStagingRowsHandler(setup.Auth, setup.Store);
        var first = await handler.HandleAsync(new(batch.Id, Limit: 2), Token); Assert.True(first.HasMore); Assert.Equal([2, 3], first.Rows.Select(row => row.RowNumber));
        var last = await handler.HandleAsync(new(batch.Id, Offset: 2, Limit: 2), Token); Assert.False(last.HasMore); Assert.Equal(4, Assert.Single(last.Rows).RowNumber);
        var errors = await handler.HandleAsync(new(batch.Id, "invalid", "official_catalog.strength_ambiguous"), Token); Assert.Single(errors.Rows);
        await Assert.ThrowsAsync<ApplicationErrorException>(() => handler.HandleAsync(new(batch.Id, Limit: 101), Token));
        await Assert.ThrowsAsync<ApplicationErrorException>(() => handler.HandleAsync(new(batch.Id, Offset: -1), Token));
        Assert.Equal(3, first.Batch.TotalRows); Assert.DoesNotContain("Ingredients", JsonSerializer.Serialize(Assert.Single(setup.Observer.Items)), StringComparison.Ordinal);
    }
    [Fact]
    public async Task InputSpoolIsRemovedAndOriginalUploadStreamRemainsOpen()
    {
        var options = new OfficialCatalogImportOptions { TemporaryDirectory = Path.Combine(AppContext.BaseDirectory, "spool-test-" + Guid.NewGuid().ToString("N")) };
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(Csv(Fields())));
        await using (var file = await new OfficialCatalogFilePreparer().PrepareAsync(input, options, Token)) Assert.True(file.Content.CanSeek);
        Assert.True(input.CanRead); Assert.Empty(Directory.EnumerateFiles(options.TemporaryDirectory, "*.tmp"));
    }
}
