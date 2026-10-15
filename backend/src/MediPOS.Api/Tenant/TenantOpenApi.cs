using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using MediPOS.Application.Modules.Reporting.Operational;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace MediPOS.Api.Tenant;

// Query contracts describe the same explicit parsers used by the endpoints, rather than
// relying on inferred binding (which would produce different HTTP errors).
public static class TenantOpenApi
{
    public sealed record QueryContract(Type Type);
    public static RouteHandlerBuilder WithQuery<T>(this RouteHandlerBuilder builder) => builder.WithMetadata(new QueryContract(typeof(T)));

    public static void Configure(OpenApiOptions options)
    {
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Components ??= new(); document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes["MediPosSession"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Cookie,
                Name = Authentication.GoogleAuthenticationRegistration.SessionCookie,
                Description = "HttpOnly Google-authenticated MediPOS session. Browsers send it with same-origin credentials; never copy it to script storage."
            };
            return Task.CompletedTask;
        });
        options.AddSchemaTransformer((schema, context, _) =>
        {
            var nullable = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type);
            var type = nullable ?? context.JsonTypeInfo.Type;
            if (type == typeof(decimal)) { schema.Type = JsonSchemaType.Number | (nullable is null ? 0 : JsonSchemaType.Null); schema.Format = "decimal"; }
            if (type == typeof(Guid)) { schema.Type = JsonSchemaType.String | (nullable is null ? 0 : JsonSchemaType.Null); schema.Format = "uuid"; }
            if (type == typeof(Microsoft.AspNetCore.Mvc.ProblemDetails))
            {
                schema.Properties ??= new Dictionary<string, IOpenApiSchema>(); schema.Required ??= new HashSet<string>();
                schema.Properties["code"] = new OpenApiSchema { Type = JsonSchemaType.String };
                schema.Properties["traceId"] = new OpenApiSchema { Type = JsonSchemaType.String };
                schema.Required.Add("code"); schema.Required.Add("traceId");
            }
            return Task.CompletedTask;
        });
        options.AddOperationTransformer(async (operation, context, token) =>
        {
            if (!context.Description.RelativePath!.StartsWith("api/", StringComparison.Ordinal)) return;
            operation.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("MediPosSession", context.Document)] = [] }];
            operation.Parameters ??= [];
            // Route values are read through the strict UUID boundary, not inferred CLR parameters.
            foreach (var parameter in operation.Parameters.OfType<OpenApiParameter>().Where(value => value.In == ParameterLocation.Path))
                parameter.Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" };
            if (context.Description.ActionDescriptor.EndpointMetadata.OfType<QueryContract>().LastOrDefault() is { } contract)
            {
                foreach (var property in contract.Type.GetProperties())
                {
                    var schema = await context.GetOrCreateSchemaAsync(property.PropertyType, cancellationToken: token);
                    if (property.GetCustomAttributes(typeof(RangeAttribute), true).OfType<RangeAttribute>().SingleOrDefault() is { } range)
                    {
                        schema = new OpenApiSchema
                        {
                            Type = JsonSchemaType.Integer,
                            Format = "int32",
                            Minimum = Convert.ToString(range.Minimum, System.Globalization.CultureInfo.InvariantCulture),
                            Maximum = Convert.ToString(range.Maximum, System.Globalization.CultureInfo.InvariantCulture)
                        };
                    }
                    operation.Parameters.Add(new OpenApiParameter
                    {
                        Name = JsonNamingPolicy.CamelCase.ConvertName(property.Name),
                        In = ParameterLocation.Query,
                        Schema = schema,
                        Required = property.IsDefined(typeof(RequiredAttribute), true)
                    });
                }
            }
            if (context.Description.HttpMethod != "GET")
                operation.Parameters.Add(new OpenApiParameter { Name = "X-CSRF-TOKEN", In = ParameterLocation.Header, Required = true, Schema = new OpenApiSchema { Type = JsonSchemaType.String } });
            operation.Description = "Requires the authenticated MediPOS cookie and current tenant authorization. " +
                "Tenant/branch IDs select a resource; they do not grant access. Enum values are strings, monetary values are exact decimals, timestamps are UTC. " +
                "Mutations require native CSRF and exact public Origin emitted by the browser. Same-origin GET may omit Origin only with native Sec-Fetch-Site same-origin/none. " +
                "Do not set browser-controlled Origin/Sec-Fetch headers in a generated client. Recheck persisted state after ambiguous sale confirmation; do not blindly retry with a new version.";
            if (operation.OperationId == "tenant.sync.batch")
            {
                var payload = await context.GetOrCreateSchemaAsync(typeof(MediPOS.Application.Modules.OfflineSync.OfflineSalePayload), cancellationToken: token);
                var document = context.Document ?? throw new InvalidOperationException("OpenAPI document scope is required.");
                document.Components ??= new(); document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
                document.Components.Schemas.TryAdd("OfflineSalePayload", payload);
                operation.Description += " offline_sale.v1 payload is a canonical JSON string using OfflineSalePayload (schemaVersion 1); preserve its event UUID, exact numeric text, declared time and SHA256 across retries. pending acknowledges reception only; accepted acknowledges commercial posting. BatchRequestId is tracing only.";
            }
            if (operation.RequestBody?.Content is { } content)
                foreach (var entry in content.Where(value => value.Key != "application/json")) entry.Value.Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" };
            foreach (var response in operation.Responses?.Values.AsEnumerable() ?? [])
                foreach (var entry in response.Content?.Where(value => value.Key is "application/pdf" or "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet") ?? [])
                    entry.Value.Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" };
        });
    }

    public class Page { [Range(0, 10000)] public int? Offset { get; init; } [Range(1, 100)] public int? Limit { get; init; } }
    public sealed class MyTenants { public Guid? AfterTenantId { get; init; } [Range(1, 100)] public int? Limit { get; init; } }
    public sealed class Branch { public Guid? BranchId { get; init; } }
    public sealed class Search { [Required] public string Q { get; init; } = ""; [Range(1, 50)] public int? Limit { get; init; } }
    public sealed class PurchaseSearch : Page { [Required] public string Reference { get; init; } = ""; }
    public sealed class ProductPage : Page { public Guid? ProductId { get; init; } }
    public sealed class BranchPage : Page { public Guid? BranchId { get; init; } }
    public sealed class CashTransfers : Page { public Guid? DestinationBranchId { get; init; } }
    public sealed class Transfers : Page { public Guid? BranchId { get; init; } public MediPOS.Domain.Modules.Transfers.TransferStatus? Status { get; init; } }
    public sealed class Notifications : Page { public Guid? BranchId { get; init; } public bool? OnlyUnread { get; init; } }
    public sealed class AuditHistory : Page { public Guid? EntityId { get; init; } public DateTimeOffset? FromUtc { get; init; } public DateTimeOffset? ToUtc { get; init; } }
    public sealed class CommissionRules : Page { public Guid? ProductId { get; init; } public bool? IncludeInactive { get; init; } }
    public sealed class CommissionReport : Page { public Guid? BranchId { get; init; } public Guid? SellerMembershipId { get; init; } public Guid? ProductId { get; init; } [Required] public DateOnly From { get; init; } [Required] public DateOnly To { get; init; } }
    public sealed class ImportFile { public string? FileName { get; init; } }
    public sealed class ImportRows { [Range(0, MediPOS.Application.Modules.Catalog.ProductImport.ProductImportLimits.MaximumWorksheetRow)] public int? AfterRowNumber { get; init; } [Range(1, 100)] public int? Limit { get; init; } }
    public sealed class DateRange { [Required] public DateOnly From { get; init; } [Required] public DateOnly To { get; init; } public Guid? BranchId { get; init; } }
    public class ReportPeriod
    {
        public OperationalPeriodType? Period { get; init; }
        public DateOnly? From { get; init; }
        public DateOnly? To { get; init; }
        public Guid? BranchId { get; init; }
    }
    public sealed class Sales : ReportPeriod
    {
        public OwnerSalesDimension? Dimension { get; init; }
        public Guid? EmployeeMembershipId { get; init; }
        public Guid? ProductId { get; init; }
        public Guid? CategoryId { get; init; }
        public OwnerSalesSort? Sort { get; init; }
        [Range(0, 10000)] public int? Offset { get; init; }
        [Range(1, 100)] public int? Limit { get; init; }
    }
    public sealed class StockRisk : Page { public Guid? BranchId { get; init; } public Guid? ProductId { get; init; } public bool? CriticalOnly { get; init; } }
    public sealed class Expirations : Page { public Guid? BranchId { get; init; } [Required] public DateOnly From { get; init; } [Required] public DateOnly To { get; init; } }
    public sealed class Rotation : ReportPeriod { public OwnerProductRotationMode? Mode { get; init; } [Range(0, 10000)] public int? Offset { get; init; } [Range(1, 100)] public int? Limit { get; init; } }
}
