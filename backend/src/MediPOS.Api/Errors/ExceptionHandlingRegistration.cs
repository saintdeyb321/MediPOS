namespace MediPOS.Api.Errors;

public static class ExceptionHandlingRegistration
{
    public static IServiceCollection AddApplicationProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            context.ProblemDetails.Extensions.TryAdd("code",
                "http." + (context.ProblemDetails.Status ?? 500).ToString(System.Globalization.CultureInfo.InvariantCulture));
            // The default writer may replace traceId; restore our server trace shared with the handler/log.
            context.ProblemDetails.Extensions["traceId"] = ApplicationExceptionHandler.GetTraceId(context.HttpContext);
        });
        services.AddExceptionHandler<ApplicationExceptionHandler>();
        return services;
    }
}
