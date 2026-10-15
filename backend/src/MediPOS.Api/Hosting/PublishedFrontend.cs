using Microsoft.Extensions.FileProviders;

namespace MediPOS.Api.Hosting;

// Explicit published SPA hosting; the Development-only LocalFrontend contract remains unchanged.
public static class PublishedFrontend
{
    public static void MapPublishedFrontend(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("PublishedFrontend:Enabled")) return;
        var root = app.Configuration["PublishedFrontend:RootPath"];
        if (!app.Environment.IsProduction() || app.Configuration.GetValue<bool>("LocalFrontend:Enabled") ||
            root is null || !Path.IsPathFullyQualified(root) || !File.Exists(Path.Combine(root, "index.html")) ||
            !Uri.TryCreate(app.Configuration["Authentication:Google:PublicOrigin"], UriKind.Absolute, out var origin) ||
            origin.Scheme != "https" || origin.HostNameType != UriHostNameType.Dns || origin.AbsolutePath != "/" ||
            origin.Query.Length != 0 || origin.Fragment.Length != 0 || origin.UserInfo.Length != 0)
            throw new InvalidOperationException("Published frontend requires Production, an explicit build directory and an exact HTTPS origin.");

        var files = new PhysicalFileProvider(root);
        app.Lifetime.ApplicationStopped.Register(files.Dispose);
        app.MapWhen(context => !Reserved(context.Request.Path), branch =>
        {
            branch.Use(async (context, next) =>
            {
                if (!context.Request.IsHttps ||
                    !context.Request.Host.Host.Equals(origin.Host, StringComparison.OrdinalIgnoreCase) ||
                    (context.Request.Host.Port ?? 443) != origin.Port)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers.XContentTypeOptions = "nosniff";
                await next(context);
            });
            branch.UseStaticFiles(new StaticFileOptions { FileProvider = files });
            branch.Run(async context =>
            {
                if ((!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)) ||
                    Path.HasExtension(context.Request.Path.Value))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.SendFileAsync(Path.Combine(root, "index.html"), context.RequestAborted);
            });
        });
    }

    private static bool Reserved(PathString path) =>
        path.StartsWithSegments("/api") || path.StartsWithSegments("/auth") || path.StartsWithSegments("/platform") ||
        path.StartsWithSegments("/health") || path.StartsWithSegments("/openapi");
}
