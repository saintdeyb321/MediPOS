using MediPOS.Api.Errors;
using MediPOS.Infrastructure;

var app = Program.CreateApplication(args);
app.Run();

public partial class Program
{
    // The real composition is inspectable without opening sockets or connecting to PostgreSQL.
    public static WebApplication CreateApplication(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddApplicationProblemDetails();
        builder.Services.AddHealthChecks();
        builder.Services.AddInfrastructure(builder.Configuration);

        if (builder.Environment.IsDevelopment()) builder.Services.AddOpenApi();

        var application = builder.Build();
        application.UseExceptionHandler(new ExceptionHandlerOptions { SuppressDiagnosticsCallback = _ => true });
        application.UseStatusCodePages();

        if (application.Environment.IsDevelopment()) application.MapOpenApi();
        application.MapHealthChecks("/health");
        return application;
    }
}
