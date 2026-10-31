namespace ApiGateway.Services;

public interface IRedisResponseCachedService
{
    public Task<CachedResponse?> GetCachedResponse(string key);
    
    public Task<bool> SetCachedResponse(string key, CachedResponse cachedResponse,TimeSpan expiration);
}