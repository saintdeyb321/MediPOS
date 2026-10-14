using MediPOS.Api.Errors;
using MediPOS.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Logging;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace MediPOS.Api.Authentication;

public static class GoogleAuthenticationRegistration
{
    public const string SessionScheme = "MediPOS.Session";
    public const string GoogleScheme = "MediPOS.Google";
    public const string SessionCookie = "__Host-MediPOS.Session";

    public static GoogleAuthenticationSettings? AddGoogleAuthentication(this WebApplicationBuilder builder)
    {
        var settings = GoogleAuthenticationSettings.Read(builder.Configuration, builder.Environment.ContentRootPath);
        if (settings is null) return null;
        var certificate = settings.LoadKeyEncryptionCertificate();
        builder.Services.AddSingleton<System.Security.Cryptography.X509Certificates.X509Certificate2>(_ => certificate);
        builder.Services.AddSingleton(settings);
        builder.Services.AddDataProtection().SetApplicationName("MediPOS.Authentication")
            .PersistKeysToFileSystem(new DirectoryInfo(settings.KeyRingPath)).ProtectKeysWithCertificate(certificate);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<VerifiedGoogleIdentitySource>();
        builder.Services.AddIdentityAuthentication<VerifiedGoogleIdentitySource, AuthenticatedMediPosUser>();
        // Both contracts must use the same scoped source populated only by TicketReceived.
        builder.Services.AddScoped<MediPOS.Application.Modules.IdentityAccess.Authentication.IVerifiedGoogleIdentitySource>(provider => provider.GetRequiredService<VerifiedGoogleIdentitySource>());
        builder.Services.AddScoped<GoogleAuthenticationEvents>();
        builder.Services.AddSingleton<ServerSessionTicketStore>();
        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = "__Host-MediPOS.Antiforgery";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.Path = "/";
        });
        IdentityModelEventSource.ShowPII = false;
        IdentityModelEventSource.LogCompleteSecurityArtifact = false;
        builder.Services.AddAuthentication(SessionScheme).AddCookie(SessionScheme, options =>
        {
            options.Cookie.Name = SessionCookie;
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(settings.SessionMinutes);
            options.SlidingExpiration = false;
            options.Events.OnRedirectToLogin = context => AuthProblem.WriteAsync(context.HttpContext, 401, "auth.session_required");
            options.Events.OnRedirectToAccessDenied = context => AuthProblem.WriteAsync(context.HttpContext, 403, "auth.denied");
        }).AddOpenIdConnect(GoogleScheme, options =>
        {
            options.Authority = "https://accounts.google.com";
            options.ClientId = settings.ClientId;
            options.ClientSecret = settings.ClientSecret;
            options.SignInScheme = SessionScheme;
            options.CallbackPath = GoogleAuthenticationSettings.CallbackPath;
            options.RemoteSignOutPath = PathString.Empty;
            options.SignedOutCallbackPath = PathString.Empty;
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.ResponseMode = OpenIdConnectResponseMode.Query;
            options.UsePkce = true;
            options.RequireHttpsMetadata = true;
            options.MapInboundClaims = false;
            options.SaveTokens = false;
            options.GetClaimsFromUserInfoEndpoint = false;
            options.UseTokenLifetime = false;
            options.RemoteAuthenticationTimeout = TimeSpan.FromMinutes(10);
            options.BackchannelTimeout = TimeSpan.FromSeconds(10);
            options.Scope.Clear(); options.Scope.Add("openid"); options.Scope.Add("email"); options.Scope.Add("profile");
            options.ProtocolValidator.RequireNonce = true;
            options.ProtocolValidator.RequireState = true;
            options.ProtocolValidator.RequireTimeStampInNonce = true;
            options.ProtocolValidator.NonceLifetime = TimeSpan.FromMinutes(10);
            options.NonceCookie.SecurePolicy = CookieSecurePolicy.Always;
            options.NonceCookie.SameSite = SameSiteMode.Lax;
            options.NonceCookie.HttpOnly = true;
            options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
            options.CorrelationCookie.SameSite = SameSiteMode.Lax;
            options.CorrelationCookie.HttpOnly = true;
            options.TokenValidationParameters.ValidIssuers = ["https://accounts.google.com", "accounts.google.com"];
            options.TokenValidationParameters.ValidateIssuer = true;
            options.TokenValidationParameters.ValidAudience = settings.ClientId;
            options.TokenValidationParameters.ValidateAudience = true;
            options.TokenValidationParameters.ValidateLifetime = true;
            options.TokenValidationParameters.RequireExpirationTime = true;
            options.TokenValidationParameters.RequireSignedTokens = true;
            options.TokenValidationParameters.ValidateIssuerSigningKey = true;
            options.TokenValidationParameters.ValidAlgorithms = ["RS256"];
            options.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(60);
            options.EventsType = typeof(GoogleAuthenticationEvents);
        });
        builder.Services.AddOptions<CookieAuthenticationOptions>(SessionScheme).Configure<ServerSessionTicketStore>((options, store) => options.SessionStore = store);
        builder.Services.AddAuthorization();
        builder.Services.PostConfigure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = settings.BehindProxy ? ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor : ForwardedHeaders.None;
            options.ForwardLimit = 1;
            options.RequireHeaderSymmetry = true;
            options.KnownProxies.Clear(); options.KnownIPNetworks.Clear();
            foreach (var proxy in settings.TrustedProxies)
                options.KnownIPNetworks.Add(new(proxy, proxy.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128));
        });
        builder.Services.AddRateLimiter(AuthenticationRateLimits.Configure);
        return settings;
    }

    public static void UseGoogleAuthenticationBoundary(this WebApplication app, GoogleAuthenticationSettings settings, bool protectPlatform = false)
    {
        // Resolve through DI so the certificate is disposed with the host.
        _ = app.Services.GetRequiredService<System.Security.Cryptography.X509Certificates.X509Certificate2>();
        // Verify the socket peer before native forwarding replaces RemoteIpAddress.
        var forwardedClient = new object();
        bool Protected(HttpContext context) => context.Request.Path.StartsWithSegments("/auth") || protectPlatform && context.Request.Path.StartsWithSegments("/platform");
        app.Use(async (context, next) =>
        {
            if (!Protected(context)) { await next(context); return; }
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.Pragma = "no-cache";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.OnStarting(() =>
            {
                // The central exception middleware clears response headers before writing errors.
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                return Task.CompletedTask;
            });
            if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodyLimit)
                bodyLimit.MaxRequestBodySize = context.Request.Path.StartsWithSegments("/platform") ? Platform.PlatformLicenseEndpoints.MaximumBodyBytes : 16_384;
            if (settings.BehindProxy)
            {
                var peer = context.Connection.RemoteIpAddress;
                if (peer?.IsIPv4MappedToIPv6 == true) peer = peer.MapToIPv4();
                var forwarded = context.Request.Headers["X-Forwarded-For"];
                if (peer is null || !settings.TrustedProxies.Contains(peer) || forwarded.Count != 1 ||
                    !System.Net.IPAddress.TryParse(forwarded[0], out var client) ||
                    client.Equals(System.Net.IPAddress.Any) || client.Equals(System.Net.IPAddress.IPv6Any) ||
                    context.Request.Headers["X-Forwarded-Proto"].Count != 1 || context.Request.Headers["X-Forwarded-Proto"] != "https")
                { await AuthProblem.WriteAsync(context, 403, "auth.origin_denied"); return; }
                context.Items[forwardedClient] = client.IsIPv4MappedToIPv6 ? client.MapToIPv4() : client;
            }
            await next(context);
        });
        app.UseForwardedHeaders();
        app.Use(async (context, next) =>
        {
            if (!Protected(context)) { await next(context); return; }
            var callback = context.Request.Path == GoogleAuthenticationSettings.CallbackPath;
            var platformRequest = protectPlatform && context.Request.Path.StartsWithSegments("/platform");
            var proxy = context.Connection.RemoteIpAddress;
            if (proxy?.IsIPv4MappedToIPv6 == true) proxy = proxy.MapToIPv4();
            if (!context.Request.IsHttps || !string.Equals(context.Request.Host.Value, settings.Origin.Authority, StringComparison.OrdinalIgnoreCase) ||
                (settings.BehindProxy ? proxy is null || !proxy.Equals(context.Items[forwardedClient]) :
                    proxy is null || context.Features.Get<ITlsConnectionFeature>() is null) ||
                (!callback && (context.Request.Headers.Origin.Count > 0 && context.Request.Headers.Origin != settings.Origin.GetLeftPart(UriPartial.Authority) ||
                    context.Request.Headers["Sec-Fetch-Site"] == "cross-site")) || (callback && context.Request.Method != "GET"))
            { await AuthProblem.WriteAsync(context, 403, "auth.origin_denied"); return; }
            if (platformRequest && (context.Request.Headers.Origin != settings.Origin.GetLeftPart(UriPartial.Authority) ||
                context.Request.Headers["Sec-Fetch-Site"].ToString() is not ("" or "same-origin" or "none")))
            { await AuthProblem.WriteAsync(context, 403, "auth.origin_denied"); return; }
            await next(context);
        });
    }

    public static void MapGoogleAuthentication(this WebApplication app)
    {
        app.MapGet("/auth/csrf", (HttpContext context, IAntiforgery csrf) =>
            Results.Json(new { requestToken = csrf.GetAndStoreTokens(context).RequestToken })).WithName("medipos.auth.csrf");
        app.MapPost("/auth/login", async (HttpContext context, IAntiforgery csrf) =>
        {
            if (!await ValidCsrf(context, csrf)) return;
            if (context.Request.Query.TryGetValue("returnUrl", out var target) && target != "/")
            { await AuthProblem.WriteAsync(context, 400, "auth.redirect_denied"); return; }
            // Revoke the previous session before starting a new round trip; callback creates a fresh session key.
            await context.SignOutAsync(SessionScheme);
            await context.ChallengeAsync(GoogleScheme, new AuthenticationProperties { RedirectUri = "/" });
        }).WithName("medipos.auth.login");
        app.MapGet(GoogleAuthenticationSettings.CallbackPath, (HttpContext context) => AuthProblem.WriteAsync(context, 401, "auth.google_failed"))
            .WithName("medipos.auth.callback");
        app.MapGet("/auth/me", (MediPOS.Application.Modules.IdentityAccess.Authentication.IAuthenticatedMediPosUser user) => Results.Json(new { userId = user.UserId }))
            .RequireAuthorization().WithName("medipos.auth.me");
        app.MapPost("/auth/logout", async (HttpContext context, IAntiforgery csrf) =>
        {
            if (!await ValidCsrf(context, csrf)) return;
            await context.SignOutAsync(SessionScheme);
            context.Response.StatusCode = StatusCodes.Status204NoContent;
        }).WithName("medipos.auth.logout");
    }
    private static async Task<bool> ValidCsrf(HttpContext context, IAntiforgery csrf)
    {
        try { await csrf.ValidateRequestAsync(context); return true; }
        catch (AntiforgeryValidationException) { await AuthProblem.WriteAsync(context, 400, "auth.csrf_invalid"); return false; }
    }
}

internal static class AuthProblem
{
    internal static Task WriteAsync(HttpContext context, int status, string code)
    {
        context.Response.StatusCode = status;
        context.Response.Headers.CacheControl = "no-store";
        var problem = new ProblemDetails { Status = status, Title = "Authentication request could not be completed." };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = ApplicationExceptionHandler.GetTraceId(context);
        return context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", context.RequestAborted);
    }
}
