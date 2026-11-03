using ApiGateway.Configuration;
using ApiGateway.Services;
using Microsoft.Extensions.Options;

namespace ApiGateway.Middlewares;

public class DistributedResponseCachingMiddleware(
    RequestDelegate next,
    ILogger<DistributedResponseCachingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext httpContext, IRedisResponseCachedService redisService,
        IOptionsMonitor<ResponseCachingOptions> responseCachingOptions)
    {
        logger.LogInformation("Distribute Response Caching.");
        
        var isGet = HttpMethods.IsGet(httpContext.Request.Method);
        if (!isGet)
        {
            logger.LogInformation(
                $"Request {httpContext.Request.Path} returned no response cached service. Not Get Method.");
            await next(httpContext);
            return;
        }

        var config = responseCachingOptions.CurrentValue;
        if (!config.Enabled)
        {
            logger.LogWarning(
                $"Request {httpContext.Request.Path} returned no response cached service. Caching disabled.");
            await next(httpContext);
            return;
        }

        var normalizedPath = httpContext.Request.Path.ToString().ToLowerInvariant();
        var isPathRegister = config.Routes.Contains<string>(normalizedPath);
        if (!isPathRegister)
        {
            logger.LogInformation(
                $"Request {httpContext.Request.Path} returned no response cached service. Path not available.");
            await next(httpContext);
            return;
        }

        // Incluir QueryString normalizado en la clave de caché para evitar colisiones en filtros (?category=...)
        var queryString = httpContext.Request.QueryString.HasValue
            ? httpContext.Request.QueryString.Value!.ToLowerInvariant()
            : string.Empty;
        var cacheKey = $"{normalizedPath}{queryString}";

        try
        {
            logger.LogInformation($"Request {cacheKey} returned a response cached service.");
            var response = await redisService.GetCachedResponse(cacheKey);

            if (response != null)
            {
                logger.LogInformation($"Request {cacheKey} Response cached service.");
                httpContext.Response.StatusCode = StatusCodes.Status200OK;
                httpContext.Response.Headers["X-Cache"] = "HIT";
                httpContext.Response.ContentType = response.ContentType;
                await httpContext.Response.WriteAsync(response.Body);
                return;
            }
            logger.LogInformation($"Request {cacheKey} cached response not found.");
        }
        catch (Exception ex)
        {
            logger.LogError($"Request {cacheKey} returned an error: {ex.Message}");
        }

        var originalBodyStream = httpContext.Response.Body;
        using var memoryStream = new MemoryStream();
        httpContext.Response.Body = memoryStream;
        try
        {
            logger.LogInformation($"Request {cacheKey} no cache found. Searching original service.");
            await next(httpContext);
            httpContext.Response.Headers["X-Cache"] = "MISS";

            if (httpContext.Response.StatusCode == StatusCodes.Status200OK)
            {
                logger.LogInformation($"Request {cacheKey} saving new cache response.");
                memoryStream.Position = 0;
                using var reader = new StreamReader(memoryStream, leaveOpen: true);
                var body = await reader.ReadToEndAsync();
                var cacheResponse =
                    new CachedResponse(body, httpContext.Response.ContentType ?? "application/octet-stream");
                await redisService.SetCachedResponse(cacheKey, cacheResponse,
                    TimeSpan.FromSeconds(config.DefaultTtlSeconds));
            }

            memoryStream.Position = 0;
            await memoryStream.CopyToAsync(originalBodyStream);
            
        }
        catch (Exception ex)
        {
            logger.LogError($"Request {cacheKey} cache error: {ex.Message}");
        }
        finally
        {
            httpContext.Response.Body = originalBodyStream;
        }
    }
}
