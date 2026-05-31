namespace Aethra.Api.Bootstrap;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => Results.Ok(new
        {
            status = "ok",
            service = "aethra-api",
            time = DateTimeOffset.UtcNow,
            version = typeof(HealthEndpoints).Assembly.GetName().Version?.ToString() ?? "0.0.0"
        }))
        .WithName("Health")
        .WithTags("System")
        .AllowAnonymous();

        return app;
    }
}
