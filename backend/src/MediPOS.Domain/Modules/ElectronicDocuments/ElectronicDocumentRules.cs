using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace MediPOS.Domain.Modules.ElectronicDocuments;

public static partial class ElectronicDocumentRules
{
    // A preparation profile, NOT a SUNAT emission/UBL profile. No tax rates or dose/price conversions.
    public const string Profile = "preparation_v1";
    public const decimal MaximumMoney = 99999999999999.9999m;
    public static void Require([DoesNotReturnIf(false)] bool condition, string code)
    { if (!condition) throw new ElectronicDocumentException(code); }
    public static void Id(Guid id) => Require(id != Guid.Empty, "identifier_invalid");
    public static void Utc(DateTimeOffset at) => Require(at.Offset == TimeSpan.Zero && at > DateTimeOffset.MinValue && at < DateTimeOffset.MaxValue, "utc_required");
    public static void Text(string? text, int limit, string code) => Require(!string.IsNullOrWhiteSpace(text) && text.Length <= limit && !text.Any(char.IsControl), code);
    public static void Money(decimal amount) => Require(amount >= 0 && amount <= MaximumMoney && decimal.Round(amount, 4) == amount, "amount_invalid");
    public static void Series(string profile, string code)
    {
        Require(profile == Profile, "profile_unsupported");
        Require(code is not null && SeriesPattern().IsMatch(code), "series_invalid");
    }
    [GeneratedRegex("^[A-Z][A-Z0-9]{0,11}$", RegexOptions.CultureInvariant)]
    private static partial Regex SeriesPattern();
}

public sealed class ElectronicDocumentException(string code) : Exception(code)
{ public string Code { get; } = code; }

// Codes are stored literally; their names/ordinals are never persisted.
public static class ElectronicDocumentTypes
{
    public const string Receipt = "receipt", Invoice = "invoice";
    public static void Validate(string type) => ElectronicDocumentRules.Require(type is Receipt or Invoice, "type_invalid");
}

public static class ElectronicDocumentStates
{
    public const string Pending = "pending", Reserved = "reserved", Dispatching = "dispatching",
        Received = "provider_received", Accepted = "authority_accepted", Rejected = "rejected",
        Retry = "retry_wait", Uncertain = "uncertain", Manual = "manual_review", Simulated = "simulated";
    public static bool IsTerminal(string state) => state is Accepted or Rejected or Simulated;
}

public enum DeliveryOutcome { Received, AuthorityAccepted, Rejected, TemporaryNotReceived, Indeterminate, Simulated }

public static class DeliveryPolicy
{
    public const int MaximumAttempts = 5;
    public static TimeSpan Backoff(int attempt)
    {
        ElectronicDocumentRules.Require(attempt is >= 1 and <= MaximumAttempts, "attempt_invalid");
        return TimeSpan.FromSeconds(Math.Min(900, 30 * (1 << (attempt - 1))));
    }
}
