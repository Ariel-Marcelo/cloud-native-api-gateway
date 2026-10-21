using System.Net;
using System.Text.Json;
using ApiGateway.Configuration;
using ApiGateway.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ApiGateway.Middlewares;

public class DistributedRateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<DistributedRateLimitingMiddleware> _logger;

    public DistributedRateLimitingMiddleware(RequestDelegate next, ILogger<DistributedRateLimitingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context, 
        IRedisRateLimiterService rateLimiterService, 
        IOptions<RateLimitingOptions> options)
    {
        var config = options.Value;

        // Omitir endpoints de salud y métricas para no bloquear chequeos de infraestructura
        if (!config.Enabled || context.Request.Path.StartsWithSegments("/healthz"))
        {
            await _next(context);
            return;
        }

        // Identificar al cliente: cabecera X-Forwarded-For (si viene detrás de proxy/CDN) o IP remota
        var clientIp = context.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim();
        if (string.IsNullOrWhiteSpace(clientIp))
        {
            clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown-client";
        }

        var result = await rateLimiterService.CheckRateLimitAsync(clientIp, config.PermitLimit, config.WindowSeconds);

        // Añadir cabeceras informativas de Rate Limiting estándar
        context.Response.Headers["X-RateLimit-Limit"] = config.PermitLimit.ToString();
        context.Response.Headers["X-RateLimit-Remaining"] = Math.Max(0, result.Remaining).ToString();

        if (!result.IsAllowed)
        {
            _logger.LogWarning("Rate limit excedido para IP '{ClientIp}'. Ventana: {Window}s, Límite: {Limit}. Retry-After: {RetryAfter}s",
                clientIp, config.WindowSeconds, config.PermitLimit, result.RetryAfterSeconds);

            context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
            context.Response.ContentType = "application/problem+json";
            context.Response.Headers["Retry-After"] = result.RetryAfterSeconds.ToString();

            var problemDetails = new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc6585#section-4",
                Title = "Too Many Requests",
                Status = StatusCodes.Status429TooManyRequests,
                Detail = $"Has excedido el límite de solicitudes ({config.PermitLimit} peticiones por cada {config.WindowSeconds} segundos). Por favor intenta de nuevo en {result.RetryAfterSeconds} segundos.",
                Instance = context.Request.Path
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(problemDetails));
            return;
        }

        await _next(context);
    }
}
