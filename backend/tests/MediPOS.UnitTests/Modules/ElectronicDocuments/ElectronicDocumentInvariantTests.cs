using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.ElectronicDocuments;
using MediPOS.Application.Modules.SalesPos;
using MediPOS.Domain.Modules.ElectronicDocuments;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.SalesPos;
using MediPOS.TestFixtures;

namespace MediPOS.UnitTests.Modules.ElectronicDocuments;

public sealed class ElectronicDocumentInvariantTests
{
    private static DateTimeOffset Now => ElectronicDocumentTestData.Now;
    [Fact]
    public void FrozenPreparationKeepsHistoricalBaseUnitPricesUnicodeExactAmountsAndTicketIndependent()
    {
        var data = new ElectronicDocumentTestData(); var document = data.Create();
        var ticket = InternalTicket.From(new(data.Sale, [data.Payment], new(data.Tenant, "Botica", data.Legal.Id, data.Legal.LegalName,
            data.Legal.Ruc, data.Branch.Id, data.Branch.Name, data.Membership, Guid.CreateVersion7(), "Staff")));
        var before = JsonSerializer.Serialize(data.Sale); data.Product.UpdatePrices(999, null);
        using var json = JsonDocument.Parse(document.SnapshotJson);
        var line = json.RootElement.GetProperty("sale").GetProperty("lines")[0];
        Assert.Equal("BASE_UNIT", line.GetProperty("unitPriceBasis").GetString()); Assert.Equal(2.125m, line.GetProperty("UnitPriceSnapshot").GetDecimal());
        Assert.Equal("retail", line.GetProperty("priceKind").GetString());
        Assert.Equal(42.5m, json.RootElement.GetProperty("monetary").GetProperty("total").GetDecimal());
        Assert.Equal("José Muñoz", json.RootElement.GetProperty("customer").GetProperty("Name").GetString());
        Assert.Equal(before, JsonSerializer.Serialize(data.Sale)); Assert.Equal("INTERNAL_TICKET", ticket.DocumentType);
        Assert.Equal("TEST1-1", document.DocumentNumber); Assert.Equal(2, data.Series.NextNumber); Assert.Equal(7, document.Id.Version);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DraftAndVoidedCannotPrepare(bool voided)
    {
        var data = new ElectronicDocumentTestData(voided); if (voided) data.Sale.Void("Error", Guid.CreateVersion7(), Now);
        Assert.Equal("confirmed_sale_required", Assert.Throws<ElectronicDocumentException>(() => data.Create()).Code);
        Assert.Equal(1, data.Series.NextNumber);
    }
    [Fact]
    public void EquivalentDecimalScaleAndLineOrderHaveStableFingerprintButCustomerOrAmountsDoNot()
    {
        var data = new ElectronicDocumentTestData(); var original = ElectronicDocumentPreparation.Fingerprint(data.Command);
        Assert.Equal(original, ElectronicDocumentPreparation.Fingerprint(data.Command with { Fiscal = data.Input with { Lines = [data.Input.Lines[0] with { NetAmount = 42.5000m }] } }));
        Assert.NotEqual(original, ElectronicDocumentPreparation.Fingerprint(data.Command with { Fiscal = data.Input with { Customer = data.Input.Customer with { Name = "Different" } } }));
        Assert.NotEqual(original, ElectronicDocumentPreparation.Fingerprint(data.Command with { Type = ElectronicDocumentTypes.Invoice }));
    }
    [Theory]
    [InlineData("currency")]
    [InlineData("profile")]
    [InlineData("customer")]
    [InlineData("identity")]
    [InlineData("net")]
    [InlineData("negative")]
    [InlineData("precision")]
    [InlineData("missing_line")]
    [InlineData("duplicate_line")]
    [InlineData("tax_missing")]
    public void MissingOrInconsistentFiscalDataFailsWithoutChangingSaleOrAllocating(string defect)
    {
        var data = new ElectronicDocumentTestData(); var input = data.Input;
        input = defect switch
        {
            "currency" => input with { Currency = "USD" },
            "profile" => input with { Profile = "sunat" },
            "customer" => input with { Customer = input.Customer with { Address = "" } },
            "identity" => input with { Customer = input.Customer with { IdentityNumber = "" } },
            "net" => input with { Lines = [input.Lines[0] with { NetAmount = 1 }] },
            "negative" => input with { Lines = [input.Lines[0] with { TaxAmount = -1 }] },
            "precision" => input with { Lines = [input.Lines[0] with { NetAmount = 42.50001m }] },
            "missing_line" => input with { Lines = [] },
            "duplicate_line" => input with { Lines = [input.Lines[0], input.Lines[0]] },
            _ => input with { Lines = [input.Lines[0] with { TaxClassification = "" }] },
        };
        var before = JsonSerializer.Serialize(data.Sale);
        Assert.Throws<ElectronicDocumentException>(() => ElectronicDocumentPreparation.Snapshot(data.Data, input));
        Assert.Equal(before, JsonSerializer.Serialize(data.Sale)); Assert.Equal(1, data.Series.NextNumber);
    }
    [Theory]
    [InlineData(DeliveryOutcome.Received, "provider_received")]
    [InlineData(DeliveryOutcome.AuthorityAccepted, "authority_accepted")]
    [InlineData(DeliveryOutcome.Rejected, "rejected")]
    [InlineData(DeliveryOutcome.TemporaryNotReceived, "retry_wait")]
    [InlineData(DeliveryOutcome.Indeterminate, "uncertain")]
    [InlineData(DeliveryOutcome.Simulated, "simulated")]
    public void TypedOutcomesHaveExplicitStatesAndAppendOnlyAttemptEvents(DeliveryOutcome outcome, string expected)
    {
        var doc = new ElectronicDocumentTestData().Create(); var claim = doc.Claim(Now, TimeSpan.FromMinutes(1));
        var start = ElectronicDocumentAttempt.Record(doc, false, Now); doc.BeginDispatch(claim, true, Now);
        doc.Complete(claim, outcome, "REF1", "test_result", outcome == DeliveryOutcome.AuthorityAccepted ? new string('a', 64) : null, Now);
        var completion = ElectronicDocumentAttempt.Record(doc, true, Now);
        Assert.Equal(expected, doc.Status); Assert.Equal("reserved", start.Outcome); Assert.Equal("started", start.Phase); Assert.Null(start.FinishedAtUtc);
        Assert.Equal(expected, completion.Outcome); Assert.Equal(start.IdempotencyKey, completion.IdempotencyKey); Assert.Equal(start.Number, completion.Number);
        Assert.Null(doc.ClaimToken); Assert.Null(doc.LeaseExpiresAtUtc);
        if (outcome is DeliveryOutcome.Received or DeliveryOutcome.AuthorityAccepted or DeliveryOutcome.Simulated) Assert.Null(doc.LastErrorCode);
        if (expected == ElectronicDocumentStates.Retry) Assert.Equal(Now.AddSeconds(30), doc.NextAttemptAtUtc); else Assert.Null(doc.NextAttemptAtUtc);
        Assert.Throws<ElectronicDocumentException>(() => doc.Complete(claim, outcome, "REF", "test", null, Now));
    }
    [Fact]
    public void NoAcceptanceWithoutEvidenceAndRejectedTransitionDoesNotMutateEvenTimestamp()
    {
        var doc = new ElectronicDocumentTestData().Create(); var claim = doc.Claim(Now, TimeSpan.FromMinutes(1)); doc.BeginDispatch(claim, true, Now);
        var before = JsonSerializer.Serialize(doc);
        Assert.Throws<ElectronicDocumentException>(() => doc.Complete(claim, DeliveryOutcome.AuthorityAccepted, "R", "ok", null, Now.AddSeconds(1)));
        Assert.Equal(before, JsonSerializer.Serialize(doc));
    }
    [Theory]
    [InlineData(DeliveryOutcome.AuthorityAccepted)]
    [InlineData(DeliveryOutcome.Rejected)]
    [InlineData(DeliveryOutcome.Simulated)]
    public void TerminalDocumentsCannotBeReclaimedOrAdministrativelyRewritten(DeliveryOutcome outcome)
    {
        var doc = new ElectronicDocumentTestData().Create(); var claim = doc.Claim(Now, TimeSpan.FromMinutes(1)); doc.BeginDispatch(claim, true, Now);
        doc.Complete(claim, outcome, "R", "result", outcome == DeliveryOutcome.AuthorityAccepted ? new string('a', 64) : null, Now);
        Assert.True(ElectronicDocumentStates.IsTerminal(doc.Status)); var original = JsonSerializer.Serialize(doc);
        Assert.Throws<ElectronicDocumentException>(() => doc.Review(false, Now.AddSeconds(1)));
        Assert.Throws<ElectronicDocumentException>(() => doc.Claim(Now.AddSeconds(1), TimeSpan.FromMinutes(1)));
        Assert.Equal(original, JsonSerializer.Serialize(doc));
    }
    [Theory]
    [InlineData(true, "uncertain")]
    [InlineData(false, "retry_wait")]
    public void RecoveryDistinguishesCommittedDispatchIntentFromUnsentClaimAndFencesLateResponses(bool dispatched, string expected)
    {
        var doc = new ElectronicDocumentTestData().Create(); var claim = doc.Claim(Now, TimeSpan.FromMinutes(1));
        if (dispatched) doc.BeginDispatch(claim, true, Now);
        var later = Now.AddMinutes(1); doc.RecoverExpired(later); Assert.Equal(expected, doc.Status);
        Assert.Throws<ElectronicDocumentException>(() => doc.Complete(claim, DeliveryOutcome.Received, "R", "ok", null, later));
        if (dispatched) Assert.Throws<ElectronicDocumentException>(() => doc.Review(true, later));
    }
    [Fact]
    public void FiveUnequivocallyUnsentFailuresStopWithoutReusingNumberAndKeys()
    {
        var data = new ElectronicDocumentTestData(); var doc = data.Create(); var now = Now;
        for (var i = 1; i <= 5; i++)
        {
            var claim = doc.Claim(now, TimeSpan.FromMinutes(1)); doc.BeginDispatch(claim, true, now);
            doc.Complete(claim, DeliveryOutcome.TemporaryNotReceived, null, "not_received", null, now);
            Assert.Equal(i, doc.AttemptCount); if (i < 5) now = doc.NextAttemptAtUtc!.Value;
        }
        Assert.Equal("manual_review", doc.Status); Assert.Null(doc.NextAttemptAtUtc); Assert.Equal(2, data.Series.NextNumber);
        Assert.Throws<ElectronicDocumentException>(() => doc.Claim(now.AddDays(1), TimeSpan.FromMinutes(1)));
        Assert.Throws<ElectronicDocumentException>(() => doc.Review(true, now));
    }
    [Fact]
    public void VoidBeforeCutoverBlocksDeliveryAndLaterVoidKeepsProviderFact()
    {
        var first = new ElectronicDocumentTestData(); var doc = first.Create(); var claim = doc.Claim(Now, TimeSpan.FromMinutes(1));
        first.Sale.Void("Error", Guid.CreateVersion7(), Now); doc.BeginDispatch(claim, false, Now);
        Assert.Equal("manual_review", doc.Status); Assert.Null(doc.SubmittedAtUtc);
        var second = new ElectronicDocumentTestData(); var sent = second.Create(); var other = sent.Claim(Now, TimeSpan.FromMinutes(1)); sent.BeginDispatch(other, true, Now);
        second.Sale.Void("Error", Guid.CreateVersion7(), Now); sent.Complete(other, DeliveryOutcome.Received, "R", "received", null, Now);
        Assert.Equal("provider_received", sent.Status); Assert.NotNull(sent.SubmittedAtUtc); Assert.Equal(SaleStatus.Voided, second.Sale.Status);
    }
    [Theory]
    [InlineData(TenantRole.Owner, true)]
    [InlineData(TenantRole.Pharmacist, false)]
    [InlineData(TenantRole.Cashier, false)]
    public void SeriesAndAdministrativeChangesRequireOwner(TenantRole role, bool allowed)
    {
        var access = new ElectronicDocumentTestData().Access(role);
        if (allowed) ElectronicDocumentAccess.Role(access, true); else Assert.Equal("cpe.role_denied", Assert.Throws<ApplicationErrorException>(() => ElectronicDocumentAccess.Role(access, true)).Error.Code);
    }
    [Fact]
    public void CashierOnlyOwnOperationAndTenantBranchMustMatch()
    {
        var data = new ElectronicDocumentTestData(); var cashier = data.Access(TenantRole.Cashier);
        ElectronicDocumentAccess.Sale(cashier, data.Tenant, data.Branch.Id, data.Membership);
        Assert.Throws<ApplicationErrorException>(() => ElectronicDocumentAccess.Sale(cashier, data.Tenant, data.Branch.Id, Guid.CreateVersion7()));
        Assert.Throws<ApplicationErrorException>(() => ElectronicDocumentAccess.Sale(data.Access(TenantRole.Owner), Guid.CreateVersion7(), data.Branch.Id, data.Membership));
        Assert.Throws<ApplicationErrorException>(() => ElectronicDocumentAccess.Sale(cashier, data.Tenant, Guid.CreateVersion7(), data.Membership));
        ElectronicDocumentAccess.Sale(data.Access(TenantRole.Pharmacist), data.Tenant, data.Branch.Id, Guid.CreateVersion7());
    }
    [Theory]
    [InlineData("", 1)]
    [InlineData("lower", 1)]
    [InlineData("A1", 0)]
    [InlineData("A1", 100000000)]
    public void InvalidSeriesCannotAllocate(string code, long start)
    { Assert.Throws<ElectronicDocumentException>(() => ElectronicDocumentSeries.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "receipt", code, "preparation_v1", "Address", start, Now)); }
    [Fact]
    public void ExhaustedInactiveOrNonUtcSeriesFailAndAmountsAreBounded()
    {
        var series = ElectronicDocumentSeries.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "invoice", "END", "preparation_v1", "Address", 99999999, Now);
        Assert.Equal(99999999, series.Allocate(Now)); Assert.Throws<ElectronicDocumentException>(() => series.Allocate(Now));
        var data = new ElectronicDocumentTestData(); data.Series.SetActive(false, data.Membership, Now); Assert.Throws<ElectronicDocumentException>(() => data.Create());
        Assert.Throws<ElectronicDocumentException>(() => ElectronicDocumentRules.Utc(Now.ToOffset(TimeSpan.FromHours(-5))));
        Assert.Throws<ElectronicDocumentException>(() => ElectronicDocumentRules.Money(decimal.MaxValue));
    }
}
