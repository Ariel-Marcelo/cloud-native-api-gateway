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
        logger.LogInformation($"Distribute Response Caching.");
        
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

        var isPathRegister = config.Routes.Contains<string>(httpContext.Request.Path.ToString().ToLowerInvariant());
        if (!isPathRegister)
        {
            logger.LogInformation(
                $"Request {httpContext.Request.Path} returned no response cached service. Path not available.");
            await next(httpContext);
            return;
        }

        try
        {
            logger.LogInformation($"Request {httpContext.Request.Path} returned a response cached service.");
            var response = await redisService.GetCachedResponse(httpContext.Request.Path);

            if (response != null)
            {
                logger.LogInformation($"Request {httpContext.Request.Path} Response cached service.");
                httpContext.Response.StatusCode = StatusCodes.Status200OK;
                httpContext.Response.Headers["X-Cache"] = "HIT";
                httpContext.Response.ContentType = response.ContentType;
                await httpContext.Response.WriteAsync(response.Body);
                return;
            }
            logger.LogInformation($"Request {httpContext.Request.Path} cached response not found.");
        }
        catch (Exception ex)
        {
            logger.LogError($"Request {httpContext.Request.Path} returned an error: {ex.Message}");
        }

        var originalBodyStream = httpContext.Response.Body;
        using var memoryStream = new MemoryStream();
        httpContext.Response.Body = memoryStream;
        try
        {
            logger.LogInformation($"Request {httpContext.Request.Path} no cache found. Searching original service.");
            await next(httpContext);
            httpContext.Response.Headers["X-Cache"] = "MISS";

            if (httpContext.Response.StatusCode == StatusCodes.Status200OK)
            {
                logger.LogInformation($"Request {httpContext.Request.Path} saving new cache response.");
                memoryStream.Position = 0;
                using var reader = new StreamReader(memoryStream, leaveOpen: true);
                var body = await reader.ReadToEndAsync();
                var cacheResponse =
                    new CachedResponse(body, httpContext.Response.ContentType ?? "application/octet-stream");
                await redisService.SetCachedResponse(httpContext.Request.Path, cacheResponse,
                    TimeSpan.FromSeconds(config.DefaultTtlSeconds));
            }

            memoryStream.Position = 0;
            await memoryStream.CopyToAsync(originalBodyStream);
            
        }
        catch (Exception ex)
        {
            logger.LogError($"Request {httpContext.Request.Path} cache expired. Error: {ex.Message}");
        }
        finally
        {
            httpContext.Response.Body = originalBodyStream;
        }
    }
}