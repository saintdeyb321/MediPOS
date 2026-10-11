using System.Text;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog.OfficialImport;
using MediPOS.Domain.Modules.Catalog.OfficialImport;
using MediPOS.Infrastructure.Modules.Catalog.OfficialImport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static MediPOS.UnitTests.Modules.Catalog.OfficialImport.OfficialCatalogTestSetup;

namespace MediPOS.UnitTests.Modules.Catalog.OfficialImport;

public sealed class OfficialCatalogDomainAndParserTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    internal static OfficialCatalogImportBatch Batch(OfficialCatalogTestSetup setup, string version = "v1") => OfficialCatalogImportBatch.Create(
        new(Source, version, "synthetic.csv", new string('a', 64), null, OfficialCatalogInput.Format, setup.Auth.Actor, "123456789abcdef0123456789abcdef0"), setup.Time.Now);
    [Fact]
    public void CompletionRequiresEveryRowAndOldLeasesCannotWriteOrTerminateAnotherAttempt()
    {
        var setup = new OfficialCatalogTestSetup(); var batch = Batch(setup); var first = Guid.CreateVersion7(); var now = setup.Time.Now;
        batch.Start(first, setup.Auth.Actor, "123456789abcdef0123456789abcdef0", now, now.AddMinutes(2));
        batch.Progress(first, 1, 1, 1, now, now.AddMinutes(2)); Assert.Throws<InvalidOperationException>(() => batch.Complete(first, 3, now));
        batch.Fail(first, "official_catalog.persistence_failed", now); var second = Guid.CreateVersion7();
        batch.Start(second, setup.Auth.Actor, "123456789abcdef0123456789abcdef0", now, now.AddMinutes(2));
        Assert.Throws<InvalidOperationException>(() => batch.Fail(first, "official_catalog.processing_cancelled", now));
        Assert.Throws<InvalidOperationException>(() => batch.Progress(first, 1, 0, 0, now, now.AddMinutes(2)));
        batch.Complete(second, 2, now); Assert.Single(batch.Failures); Assert.Equal("staged", batch.Status);
        Assert.Throws<InvalidOperationException>(() => batch.Start(Guid.NewGuid(), setup.Auth.Actor, "123456789abcdef0123456789abcdef0", now, now.AddMinutes(2)));
    }
    [Fact]
    public async Task ASecondRequestObservesALiveAttemptAndAnExpiredAttemptCanBeRecovered()
    {
        var setup = new OfficialCatalogTestSetup(); var batch = Batch(setup); var actor = new OfficialCatalogActor(setup.Auth.Actor, "123456789abcdef0123456789abcdef0");
        var first = await setup.Store.ClaimAsync(batch, actor, setup.Time.Now, setup.Options.ProcessingLease, Token);
        var busy = await setup.Store.ClaimAsync(Batch(setup), actor, setup.Time.Now, setup.Options.ProcessingLease, Token);
        Assert.Null(busy.LeaseId); Assert.Equal(first.Batch.Id, busy.Batch.Id);
        setup.Time.Now = setup.Time.Now.AddMinutes(3);
        var recovered = await setup.Store.ClaimAsync(Batch(setup), actor, setup.Time.Now, setup.Options.ProcessingLease, Token);
        Assert.NotEqual(first.LeaseId, recovered.LeaseId); Assert.Equal(2, recovered.Batch.AttemptCount);
        Assert.Equal("official_catalog.processing_interrupted", Assert.Single(recovered.Batch.Failures).Code);
        Assert.False(await setup.Store.FailAsync(batch.Id, first.LeaseId!.Value, "official_catalog.processing_cancelled", setup.Time.Now, Token));
    }
    [Fact]
    public void RetryDiagnosticsAreBoundedByTheAttemptLimit()
    {
        var setup = new OfficialCatalogTestSetup(); var batch = Batch(setup);
        for (var index = 0; index < OfficialCatalogImportBatch.MaximumAttempts; index++)
        {
            var lease = Guid.NewGuid(); batch.Start(lease, setup.Auth.Actor, "123456789abcdef0123456789abcdef0", setup.Time.Now, setup.Time.Now.AddMinutes(1));
            batch.Fail(lease, "official_catalog.persistence_failed", setup.Time.Now);
        }
        Assert.Equal(5, batch.Failures.Count);
        Assert.Throws<InvalidOperationException>(() => batch.Start(Guid.NewGuid(), setup.Auth.Actor, "123456789abcdef0123456789abcdef0", setup.Time.Now, setup.Time.Now.AddMinutes(1)));
    }
    [Theory]
    [InlineData("id", "name", "id", "NAME", true)]
    [InlineData("id", "name", "other", " NAME ", true)]
    [InlineData("id", "name", "other", "different", false)]
    public void DuplicateSignalsDoNotMergeIdentifiersOrRewriteNames(string id, string name, string otherId, string otherName, bool duplicate)
    {
        var setup = new OfficialCatalogTestSetup(); var batch = Batch(setup); var anchors = new OfficialCatalogDuplicateAnchors();
        var first = OfficialCatalogInput.Validate(new(2, Fields(id, name)), batch.Id, setup.Time.Now);
        var second = OfficialCatalogInput.Validate(new(3, Fields(otherId, otherName)), batch.Id, setup.Time.Now);
        anchors.Observe(first); anchors.Observe(second);
        Assert.Equal(duplicate, second.DuplicateSignals.Length != 0); Assert.Equal(otherId, second.SourceRecordId); Assert.Equal(otherName, second.OriginalName);
        Assert.NotEqual(first.Id, second.Id);
    }
    [Fact]
    public async Task Utf8BomExtraColumnsAndEscapedQuotesAcrossBufferBoundariesArePreserved()
    {
        var name = new string('a', 2046) + "\"tail\"";
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(Csv(Fields(name: name)))).ToArray();
        using var stream = new MemoryStream(bytes); var rows = new List<OfficialCatalogSourceRow>();
        await foreach (var row in new OfficialCatalogCsvReader().ReadAsync(stream, new(), Token)) rows.Add(row);
        Assert.Equal(name, Assert.Single(rows).Fields["Name"]);
        var csv = Header + ",Note\n" + Csv(Fields()).Split("\r\n", StringSplitOptions.None)[1] + ",unmapped source text\n";
        using var extra = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        await foreach (var row in new OfficialCatalogCsvReader().ReadAsync(extra, new(), Token)) Assert.Equal("unmapped source text", row.Fields["Note"]);
    }
    [Theory]
    [InlineData("duplicate_header")]
    [InlineData("control_header")]
    [InlineData("too_many_columns")]
    [InlineData("quote_in_unquoted_field")]
    [InlineData("content_after_quote")]
    public async Task MalformedCsvCannotSmuggleAChangedSchema(string kind)
    {
        var csv = kind switch
        {
            "duplicate_header" => Header + ",Name\n",
            "control_header" => Header.Replace("Name", "Name\u0001", StringComparison.Ordinal) + "\n",
            "too_many_columns" => Header + "," + string.Join(',', Enumerable.Range(0, 24).Select(value => "extra" + value)) + "\n",
            "quote_in_unquoted_field" => Header + "\na\"b,x,x,x,x,x,x,x,x\n",
            _ => Header + "\n\"id\"bad,x,x,x,x,x,x,x,x\n",
        };
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        await Assert.ThrowsAsync<ApplicationErrorException>(async () => { await foreach (var row in new OfficialCatalogCsvReader().ReadAsync(input, new(), Token)) Assert.Fail("Malformed file yielded a row."); });
    }
    [Fact]
    public void PlatformRegistrationRequiresSameOperationalDatabaseAndExplicitAdapters()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:MediPosDatabase"] = "Host=localhost;Database=operational;Username=commercial",
            ["ConnectionStrings:OfficialCatalogDatabase"] = "Host=localhost;Database=another;Username=platform",
        }).Build();
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddOfficialCatalogAdministration<Authority, SourceRegistry>(config));
        config["ConnectionStrings:OfficialCatalogDatabase"] = "Host=localhost;Database=operational;Username=platform";
        var services = new ServiceCollection().AddOfficialCatalogAdministration<Authority, SourceRegistry>(config);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IOfficialCatalogAdministration) && descriptor.ImplementationType == typeof(Authority));
    }
}
