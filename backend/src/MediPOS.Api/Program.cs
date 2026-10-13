using MediPOS.Api.Authentication;
using MediPOS.Api.Errors;
using MediPOS.Api.Observability;
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
        configureServices?.Invoke(builder.Services);

        if (builder.Environment.IsDevelopment()) builder.Services.AddOpenApi();

        var application = builder.Build();
        application.UseRouting();
        application.UseMiddleware<RequestObservationMiddleware>();
        application.UseExceptionHandler(new ExceptionHandlerOptions { SuppressDiagnosticsCallback = _ => true });
        application.UseStatusCodePages();
        if (authentication is not null) application.UseGoogleAuthenticationBoundary(authentication);
        application.UseRateLimiter();
        if (authentication is not null)
        {
            application.UseAuthentication();
            application.UseAuthorization();
            application.MapGoogleAuthentication();
        }

        if (application.Environment.IsDevelopment()) application.MapOpenApi();
        application.MapBackendHealth();
        return application;
    }
}
