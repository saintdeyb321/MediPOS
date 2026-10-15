using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediPOS.Api.Errors;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Net.Http.Headers;

namespace MediPOS.Api.Tenant;

public static class TenantHttp
{
    public const int MaximumBodyBytes = 65_536;
    internal static readonly JsonSerializerOptions Json = CreateJson();

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        ConfigureJson(options);
        return options;
    }
    internal static void ConfigureJson(JsonSerializerOptions options)
    {
        options.PropertyNameCaseInsensitive = false;
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        options.AllowDuplicateProperties = false;
        options.MaxDepth = 12;
        options.NumberHandling = JsonNumberHandling.Strict;
        options.RespectNullableAnnotations = true;
        options.RespectRequiredConstructorParameters = true;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, false));
        options.Converters.Add(new IdentifierConverter());
    }

    public static Guid Actor(HttpContext context) =>
        context.RequestServices.GetRequiredService<IAuthenticatedMediPosUser>().UserId is { } id && id != Guid.Empty ? id :
            throw new HttpBoundaryException(401, "auth.session_required");
    public static Guid Tenant(HttpContext context) => Id(context, "tenantId");
    public static Guid Branch(HttpContext context) => Id(context, "branchId");
    public static Guid Id(HttpContext context, string name) => ParseId(context.Request.RouteValues[name] as string);
    public static Guid ParseId(string? text) => text is { Length: 36 } && Guid.TryParseExact(text, "D", out var id) && id != Guid.Empty ? id :
        throw new HttpBoundaryException(400, "api.invalid_identifier");
    public static string? Query(HttpContext context, string name)
    {
        var values = context.Request.Query[name];
        if (values.Count > 1) throw new HttpBoundaryException(400, "api.invalid_query");
        return values.Count == 0 ? null : values[0];
    }
    public static int Limit(HttpContext context) => BoundedCount(context, "limit", 50, 1, 100);
    public static int Offset(HttpContext context) => BoundedCount(context, "offset", 0, 0, 10_000);
    public static int BoundedCount(HttpContext context, string name, int fallback, int minimum, int maximum)
    {
        var value = Query(context, name);
        if (value is null) return fallback;
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed >= minimum && parsed <= maximum ? parsed :
            throw new HttpBoundaryException(400, "api.invalid_query");
    }
    public static Guid? OptionalId(HttpContext context, string name) => Query(context, name) is { } text ? ParseId(text) : null;
    public static DateOnly Date(HttpContext context, string name) =>
        DateOnly.TryParseExact(Query(context, name), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date :
            throw new HttpBoundaryException(400, "api.invalid_query");
    public static T Choice<T>(HttpContext context, string name, T fallback) where T : struct, Enum
    {
        var text = Query(context, name);
        if (text is null) return fallback;
        foreach (var value in Enum.GetValues<T>())
            if (string.Equals(text, JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString()), StringComparison.Ordinal)) return value;
        throw new HttpBoundaryException(400, "api.invalid_query");
    }
    public static bool Flag(HttpContext context, string name, bool fallback)
    {
        var value = Query(context, name);
        return value is null ? fallback : value switch { "true" => true, "false" => false, _ => throw new HttpBoundaryException(400, "api.invalid_query") };
    }

    public static async Task<T> Read<T>(HttpContext context)
    {
        if (context.Request.ContentLength > MaximumBodyBytes) throw new HttpBoundaryException(413, "api.body_too_large");
        if (context.Request.Headers.ContentEncoding.Count != 0 || !MediaTypeHeaderValue.TryParse(context.Request.ContentType, out var type) ||
            !string.Equals(type.MediaType.Value, "application/json", StringComparison.OrdinalIgnoreCase) ||
            type.Charset.HasValue && !string.Equals(type.Charset.Value, "utf-8", StringComparison.OrdinalIgnoreCase))
            throw new HttpBoundaryException(415, "api.json_required");
        var buffer = new byte[MaximumBodyBytes + 1];
        try
        {
            var count = await context.Request.Body.ReadAtLeastAsync(buffer, buffer.Length, false, context.RequestAborted);
            if (count > MaximumBodyBytes) throw new HttpBoundaryException(413, "api.body_too_large");
            // Nullable annotations do not validate generic collection element nullability.
            // These transport contracts have no nullable array elements; reject before handlers.
            using var document = JsonDocument.Parse(buffer.AsMemory(0, count), new() { MaxDepth = 12 });
            RequireArrayElements(document.RootElement);
            return JsonSerializer.Deserialize<T>(buffer.AsSpan(0, count), Json) ?? throw new HttpBoundaryException(400, "api.invalid_json");
        }
        catch (JsonException) { throw new HttpBoundaryException(400, "api.invalid_json"); }
        catch (BadHttpRequestException error) when (error.StatusCode is 400 or 413)
        { throw new HttpBoundaryException(error.StatusCode, error.StatusCode == 413 ? "api.body_too_large" : "api.invalid_json"); }
    }
    public static IResult Response<T>(T value, int status = 200) => Results.Json(value, Json, statusCode: status);

    private static void RequireArrayElements(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var property in value.EnumerateObject()) RequireArrayElements(property.Value);
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var element in value.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.Null) throw new JsonException();
                RequireArrayElements(element);
            }
    }

    private sealed class IdentifierConverter : JsonConverter<Guid>
    {
        public override Guid Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String) throw new JsonException();
            var text = reader.GetString();
            return text is { Length: 36 } && Guid.TryParseExact(text, "D", out var id) && id != Guid.Empty ? id : throw new JsonException();
        }
        public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString("D"));
    }
}

public sealed class TenantBoundaryFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var actor = TenantHttp.Actor(http);
        var allowedQuery = http.GetEndpoint()?.Metadata.GetMetadata<TenantOpenApi.QueryContract>()?.Type.GetProperties()
            .Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name)).ToHashSet(StringComparer.Ordinal) ?? [];
        if (http.Request.Query.Any(pair => !allowedQuery.Contains(pair.Key) || pair.Value.Count != 1))
            throw new HttpBoundaryException(400, "api.invalid_query");
        using var lease = http.RequestServices.GetRequiredService<TenantApiRateLimiter>().Acquire(actor, HttpMethods.IsGet(http.Request.Method),
            http.GetEndpoint()?.Metadata.GetMetadata<TenantRequestPolicy>()?.Heavy == true);
        if (!lease.IsAcquired) throw new HttpBoundaryException(429, "api.rate_limited");
        if (!HttpMethods.IsGet(http.Request.Method))
        {
            try { await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http); }
            catch (AntiforgeryValidationException) { throw new HttpBoundaryException(400, "auth.csrf_invalid"); }
        }
        return await next(context);
    }
}

public sealed record TenantRequestPolicy(int MaximumBodyBytes, bool Heavy);
