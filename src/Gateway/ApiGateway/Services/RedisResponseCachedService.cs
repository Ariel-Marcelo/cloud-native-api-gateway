using System.Text.Json;
using StackExchange.Redis;

namespace ApiGateway.Services;

public class RedisResponseCachedService(IConnectionMultiplexer redis, ILogger<RedisResponseCachedService> logger)
    : IRedisResponseCachedService
{
    public async Task<CachedResponse?> GetCachedResponse(string key)
    {
        try
        {
            var db = redis.GetDatabase();
            RedisValue value = await db.StringGetAsync($"cached:{key}");

            return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<CachedResponse>(value.ToString());

        }
        catch (RedisException e)
        {
            logger.LogError(e, $"Error al conectarse con Redis: {key}");
            return null;
        }
    }

    public async Task<bool> SetCachedResponse(string path, CachedResponse response, TimeSpan expiration)
    {
        try
        {
            var db = redis.GetDatabase();
            var key = $"cached:{path}";
            var serialized = JsonSerializer.Serialize(response);
            return await db.StringSetAsync(key, serialized, expiration);
        }
        catch (RedisException e)
        {
            logger.LogError(e, $"Error al comunicarse con Redis y Almacenar la respuesta cache para {path}");
            return false;
        }
    }
}