using Microsoft.Extensions.FileProviders;

namespace MediPOS.Api.Hosting;

// Development opt-in: Kestrel serves the existing Vite build, without a proxy.
public static class LocalFrontend
{
    public static void MapLocalFrontend(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("LocalFrontend:Enabled")) return;
        if (!app.Environment.IsDevelopment())
            throw new InvalidOperationException("Local frontend hosting is Development only.");
        var root = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "../../..", "frontend/dist"));
        if (!File.Exists(Path.Combine(root, "index.html")))
            throw new InvalidOperationException("Build frontend/dist before starting the local HTTPS profile.");
        var files = new PhysicalFileProvider(root);
        app.Lifetime.ApplicationStopped.Register(files.Dispose);
        app.UseWhen(context => !Reserved(context.Request.Path), branch =>
        {
            branch.Use(async (context, next) =>
            {
                if (!context.Request.IsHttps || !context.Request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers.XContentTypeOptions = "nosniff";
                await next(context);
            });
            branch.UseStaticFiles(new StaticFileOptions { FileProvider = files });
        });
        app.MapFallback(async context =>
        {
            if ((!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)) ||
                Reserved(context.Request.Path) || !context.Request.IsHttps ||
                !context.Request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            context.Response.Headers.CacheControl = "no-store";
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.SendFileAsync(Path.Combine(root, "index.html"), context.RequestAborted);
        });
    }

    private static bool Reserved(PathString path) =>
        path.StartsWithSegments("/api") || path.StartsWithSegments("/auth") || path.StartsWithSegments("/platform") ||
        path.StartsWithSegments("/health") || path.StartsWithSegments("/openapi");
}
