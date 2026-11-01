using System.Net;
using System.Text.Json;
using ApiGateway.Configuration;
using ApiGateway.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ApiGateway.Middlewares;

public class DistributedRateLimitingMiddleware(RequestDelegate next, ILogger<DistributedRateLimitingMiddleware> logger)
{
    public async Task InvokeAsync(
        HttpContext context,
        IRedisRateLimiterService rateLimiterService,
        IOptionsMonitor<RateLimitingOptions> options)
    {
        var config = options.CurrentValue;

        // Omitir endpoints de salud y métricas para no bloquear chequeos de infraestructura
        if (!config.Enabled || context.Request.Path.StartsWithSegments("/healthz") || context.Request.Path.StartsWithSegments("/metrics"))
        {
            await next(context);
            return;
        }


        var clientIdentifier = String.Empty;
        var tier = config.DefaultTier;

        if (context.User?.Identity?.IsAuthenticated == true)
        {
            var sub = context.User.FindFirst("sub")?.Value;
            if (!string.IsNullOrWhiteSpace(sub))
            {
                clientIdentifier = $"user:{sub}";
            }
            tier = context.User.FindFirst("tier")?.Value ?? tier;
        }

        if (!config.Tiers.TryGetValue(tier, out var configTier))
        {
            logger.LogWarning("No se ha definido un tier valido para '{Tier}'. Se omite el rate limiting para este cliente.", tier);
            if (!config.Tiers.TryGetValue(config.DefaultTier, out configTier))
            {
                logger.LogError("No se ha definido un tier valido para el tier por defecto '{DefaultTier}'. Se omite el rate limiting para este cliente.", config.DefaultTier);
                await NoTierConfigResponse(context, tier);
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(clientIdentifier))
        {
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown-client";
            clientIdentifier = $"ip:{ip}";
        }

        var result = await rateLimiterService.CheckRateLimitAsync(clientIdentifier, configTier.PermitLimit, configTier.WindowSeconds);

        context.Response.Headers["X-RateLimit-Limit"] = configTier.PermitLimit.ToString();
        context.Response.Headers["X-RateLimit-Remaining"] = Math.Max(0, result.Remaining).ToString();
        context.Response.Headers["X-RateLimit-Tier"] = tier;

        if (!result.IsAllowed)
        {
            logger.LogWarning(
                "Rate limit excedido para ClientIdentifier '{ClientIdentifier}'. Ventana: {Window}s, Límite: {Limit}. Retry-After: {RetryAfter}s",
                clientIdentifier, configTier.WindowSeconds, configTier.PermitLimit, result.RetryAfterSeconds);

            await TooManyRequestConfig(context, result, configTier);
            return;
        }

        await next(context);
    }

    private static async Task TooManyRequestConfig(HttpContext context, RateLimitResult result, TierQuota configTier)
    {
        context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
        context.Response.ContentType = "application/problem+json";
        context.Response.Headers["Retry-After"] = result.RetryAfterSeconds.ToString();

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
    }
}