using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace ApiGateway.Middlewares;

public class CustomResponseMiddleware(RequestDelegate next, ILogger<CustomResponseMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext httpContext)
    {
        await next(httpContext);
        
        if (httpContext.Response.HasStarted)
        {
            return;
        }
        
        switch (httpContext.Response.StatusCode)
        {
            case StatusCodes.Status504GatewayTimeout:
            {
                logger.LogInformation("Service Timeout");
                httpContext.Response.ContentType = "application/problem+json";

                var problemDetails = new ProblemDetails
                {
                    Type = "https://tools.ietf.org/html/rfc6585#section-4",
                    Title = "Service Timeout",
                    Status = StatusCodes.Status504GatewayTimeout,
                    Detail =
                        $"Actualmente el servicio no se encuentra disponible por favor intente de nuevo mas tarde.",
                    Instance = httpContext.Request.Path
                };
                await httpContext.Response.WriteAsync(JsonSerializer.Serialize(problemDetails));
                return;
            }
            case StatusCodes.Status503ServiceUnavailable:
            {
                logger.LogInformation("Service Unavailable");
                httpContext.Response.ContentType = "application/problem+json";

                var problemDetails = new ProblemDetails
                {
                    Type = "https://tools.ietf.org/html/rfc6585#section-4",
                    Title = "Service Unavailable",
                    Status = StatusCodes.Status503ServiceUnavailable,
                    Detail =
                        $"Actualmente el servicio no se encuentra disponible por favor intente de nuevo mas tarde.",
                    Instance = httpContext.Request.Path
                };
                await httpContext.Response.WriteAsync(JsonSerializer.Serialize(problemDetails));
                break;
            }
        }
    }
}