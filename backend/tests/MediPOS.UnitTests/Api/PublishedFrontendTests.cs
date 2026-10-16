using MediPOS.Api.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MediPOS.UnitTests.Api;

// Pipeline unit tests only; actual LAB HTTPS/PostgreSQL acceptance is performed separately.
public sealed class PublishedFrontendTests
{
    [Theory]
    [InlineData("/", "GET", "https", "localhost:7343", 200)]
    [InlineData("/dashboard", "GET", "https", "localhost:7343", 200)]
    [InlineData("/missing.js", "GET", "https", "localhost:7343", 404)]
    [InlineData("/dashboard", "POST", "https", "localhost:7343", 404)]
    [InlineData("/", "GET", "http", "localhost:7343", 400)]
    [InlineData("/", "GET", "https", "localhost:7243", 400)]
    [InlineData("/", "GET", "https", "localhost:7344", 400)]
    [InlineData("/", "GET", "https", "medipos-lab.localhost:7343", 400)]
    [InlineData("/api/missing", "GET", "https", "localhost:7343", 404)]
    [InlineData("/auth/missing", "GET", "https", "localhost:7343", 404)]
    [InlineData("/platform/tenants", "GET", "https", "localhost:7343", 404)]
    [InlineData("/health/unknown", "GET", "https", "localhost:7343", 404)]
    [InlineData("/openapi/v1.json", "GET", "https", "localhost:7343", 404)]
    public async Task SpaNeverMasksApiOrInvalidOrigins(string path, string method, string scheme, string host, int status)
    {
        var directory = Directory.CreateTempSubdirectory("medipos-published-test-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory.FullName, "index.html"), "<html>unit test SPA</html>", TestContext.Current.CancellationToken);
            await using var app = Create(directory.FullName);
            app.MapPublishedFrontend();
            var pipeline = ((IApplicationBuilder)app).Build();
            var context = new DefaultHttpContext { RequestServices = app.Services };
            context.Request.Path = path; context.Request.Method = method;
            context.Request.Scheme = scheme; context.Request.Host = new(host);
            await using var body = new MemoryStream(); context.Response.Body = body;
            await pipeline(context);
            Assert.Equal(status, context.Response.StatusCode);
            if (status == 200) Assert.StartsWith("text/html", context.Response.ContentType);
            else Assert.NotEqual("text/html; charset=utf-8", context.Response.ContentType);
        }
        finally { directory.Delete(true); }
    }

    [Theory]
    [InlineData("Development", false)]
    [InlineData("Production", true)]
    public async Task RejectsDevelopmentAndConflictingLocalFrontend(string environment, bool local)
    {
        var directory = Directory.CreateTempSubdirectory("medipos-published-test-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory.FullName, "index.html"), "unit", TestContext.Current.CancellationToken);
            await using var app = Create(directory.FullName, environment, local);
            Assert.Throws<InvalidOperationException>(app.MapPublishedFrontend);
        }
        finally { directory.Delete(true); }
    }

    private static WebApplication Create(string root, string environment = "Production", bool local = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PublishedFrontend:Enabled"] = "true",
            ["PublishedFrontend:RootPath"] = root,
            ["Authentication:Google:PublicOrigin"] = "https://localhost:7343",
            ["LocalFrontend:Enabled"] = local.ToString()
        });
        return builder.Build();
    }
}
