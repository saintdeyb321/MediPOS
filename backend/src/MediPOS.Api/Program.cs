using MediPOS.Api.Authentication;
using MediPOS.Api.Errors;
using MediPOS.Api.Observability;
using MediPOS.Api.Platform;
using MediPOS.Infrastructure;

var app = Program.CreateApplication(args);
app.Run();

public partial class Program
{
    // The real composition is inspectable without opening sockets or connecting to PostgreSQL.
    public static WebApplication CreateApplication(string[] args, Action<IServiceCollection>? configureServices = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddApplicationProblemDetails();
        builder.Services.AddInfrastructure(builder.Configuration);
        builder.AddBackendObservability();
        var authentication = builder.AddGoogleAuthentication();
        var platform = PlatformAdministrationSettings.Read(builder.Configuration, authentication is not null, builder.Environment.IsProduction());
        if (platform is not null)
        {
            builder.Services.AddSingleton(platform);
            builder.Services.AddScoped<MediPOS.Application.Modules.IdentityAccess.Authentication.IPlatformAdministration, SessionPlatformAdministration>();
        }
        var platformApi = PlatformApiSettings.IsEnabled(builder.Configuration, builder.Environment.IsProduction(), authentication is not null, platform is not null);
        if (platformApi) builder.Services.AddSingleton<PlatformApiRateLimiter>();
        configureServices?.Invoke(builder.Services);

        if (builder.Environment.IsDevelopment()) builder.Services.AddOpenApi();

        var application = builder.Build();
        application.UseRouting();
        application.UseMiddleware<RequestObservationMiddleware>();
        application.UseExceptionHandler(new ExceptionHandlerOptions { SuppressDiagnosticsCallback = _ => true });
        application.UseStatusCodePages();
        if (authentication is not null) application.UseGoogleAuthenticationBoundary(authentication, platformApi);
        application.UseRateLimiter();
        if (authentication is not null)
        {
            application.UseAuthentication();
            application.UseAuthorization();
            application.MapGoogleAuthentication();
            if (platformApi) application.MapPlatformLicenseEndpoints();
        }

        if (application.Environment.IsDevelopment()) application.MapOpenApi();
        application.MapBackendHealth();
        return application;
    }
}
