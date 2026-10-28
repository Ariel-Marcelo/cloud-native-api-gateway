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
        IOptionsMonitor<RateLimitingOptions> options)
    {
        var config = options.CurrentValue;

        // Omitir endpoints de salud y métricas para no bloquear chequeos de infraestructura
        if (!config.Enabled || context.Request.Path.StartsWithSegments("/healthz"))
        {
            await _next(context);
            return;
        }


        var clientIdentifier = String.Empty;
        var tier = config.DefaultTier;
        
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            clientIdentifier = $"user:{context.User.FindFirst("sub")?.Value}";
            tier = context.User.FindFirst("tier")?.Value ?? tier;
        }

        if (!config.Tiers.TryGetValue(tier, out var configTier))
        {
            _logger.LogWarning("No se ha definido un tier valido para '{Tier}'. Se omite el rate limiting para este cliente.", tier);
            if(!config.Tiers.TryGetValue(config.DefaultTier, out configTier))
            {
                _logger.LogError("No se ha definido un tier valido para el tier por defecto '{DefaultTier}'. Se omite el rate limiting para este cliente.", config.DefaultTier);
                await NoTierConfigResponse(context, tier);
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(clientIdentifier))
        {
            clientIdentifier = $"ip:{context.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()}";
            if (string.IsNullOrWhiteSpace(clientIdentifier))
            {
                clientIdentifier =  $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown-client"}";
            }
        }
        
        var result = await rateLimiterService.CheckRateLimitAsync(clientIdentifier, configTier.PermitLimit, configTier.WindowSeconds);

        // Añadir cabeceras informativas de Rate Limiting estándar
        context.Response.Headers["X-RateLimit-Limit"] = configTier.PermitLimit.ToString();
        context.Response.Headers["X-RateLimit-Remaining"] = Math.Max(0, result.Remaining).ToString();

        if (!result.IsAllowed)
        {
            _logger.LogWarning(
                "Rate limit excedido para ClientIdentifier '{ClientIdentifier}'. Ventana: {Window}s, Límite: {Limit}. Retry-After: {RetryAfter}s",
                clientIdentifier, configTier.WindowSeconds, configTier.PermitLimit, result.RetryAfterSeconds);

            await TooManyRequestConfig(context, result, configTier, tier);
            return;
        }

        await _next(context);
    }

    private static async Task TooManyRequestConfig(HttpContext context, RateLimitResult result, TierQuota configTier, string tier)
    {
        context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
        context.Response.ContentType = "application/problem+json";
        context.Response.Headers["Retry-After"] = result.RetryAfterSeconds.ToString();
        context.Response.Headers["X-RateLimit-Tier"] = tier;

        var problemDetails = new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc6585#section-4",
            Title = "Too Many Requests",
            Status = StatusCodes.Status429TooManyRequests,
            Detail =
                $"Has excedido el límite de solicitudes ({configTier.PermitLimit} peticiones por cada {configTier.WindowSeconds} segundos). Por favor intenta de nuevo en {result.RetryAfterSeconds} segundos.",
            Instance = context.Request.Path
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(problemDetails));
    }

    private static async Task NoTierConfigResponse(HttpContext context, string tier)
    {
        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        context.Response.ContentType = "application/problem+json";

        var problemDetails = new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc6585#section-4",
            Title = "No Tier Defined",
            Status = StatusCodes.Status500InternalServerError,
            Detail =
                $"No se ha definido un tier valido tier: {tier}.",
            Instance = context.Request.Path
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(problemDetails));
        return;
    }
}