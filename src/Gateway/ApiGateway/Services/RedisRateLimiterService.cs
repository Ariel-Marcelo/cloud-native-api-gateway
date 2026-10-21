using StackExchange.Redis;

namespace ApiGateway.Services;

public record RateLimitResult(bool IsAllowed, long Remaining, long RetryAfterSeconds);

public interface IRedisRateLimiterService
{
    Task<RateLimitResult> CheckRateLimitAsync(string clientIdentifier, int permitLimit, int windowSeconds);
}

public class RedisRateLimiterService : IRedisRateLimiterService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisRateLimiterService> _logger;

    // Script Lua optimizado para ejecutar el algoritmo Sliding Window Counter de forma atómica en Redis
    private const string SlidingWindowLuaScript = @"
        local key = KEYS[1]
        local now = tonumber(ARGV[1])
        local windowMs = tonumber(ARGV[2])
        local limit = tonumber(ARGV[3])
        local memberId = ARGV[4]
        local clearBefore = now - windowMs

        -- 1. Eliminar timestamps fuera de la ventana deslizante actual
        redis.call('ZREMRANGEBYSCORE', key, '-inf', clearBefore)

        -- 2. Contar solicitudes activas dentro de la ventana
        local currentCount = redis.call('ZCARD', key)

        -- 3. Verificar si el límite ha sido alcanzado
        if currentCount < limit then
            -- Registrar la nueva solicitud con score = timestamp actual
            redis.call('ZADD', key, now, memberId)
            -- Asignar TTL proporcional a la ventana para liberar memoria automáticamente
            redis.call('EXPIRE', key, math.ceil(windowMs / 1000) + 2)
            local remaining = limit - currentCount - 1
            return { 1, remaining, 0 }
        else
            -- Límite excedido: calcular tiempo restante en segundos para la siguiente ranura libre
            local oldest = redis.call('ZRANGE', key, 0, 0, 'WITHSCORES')
            local retryAfter = 1
            if #oldest > 0 then
                local oldestScore = tonumber(oldest[2])
                local timeUntilExpiry = (oldestScore + windowMs) - now
                if timeUntilExpiry > 0 then
                    retryAfter = math.ceil(timeUntilExpiry / 1000)
                end
            end
            return { 0, 0, retryAfter }
        end
    ";

    public RedisRateLimiterService(IConnectionMultiplexer redis, ILogger<RedisRateLimiterService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<RateLimitResult> CheckRateLimitAsync(string clientIdentifier, int permitLimit, int windowSeconds)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = $"ratelimit:sliding:{clientIdentifier}";
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var windowMs = windowSeconds * 1000;
            var memberId = $"{nowMs}:{Guid.NewGuid():N}";

            var keys = new RedisKey[] { key };
            var values = new RedisValue[] { nowMs, windowMs, permitLimit, memberId };

            var result = (RedisResult[]?)await db.ScriptEvaluateAsync(SlidingWindowLuaScript, keys, values);

            if (result != null && result.Length >= 3)
            {
                var isAllowed = (int)result[0] == 1;
                var remaining = (long)result[1];
                var retryAfter = (long)result[2];

                return new RateLimitResult(isAllowed, remaining, retryAfter);
            }

            // Fallback por defecto en caso de respuesta inesperada
            return new RateLimitResult(true, permitLimit - 1, 0);
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Error al comunicarse con Redis para Rate Limiting. Se permite la solicitud como fallback (Fail-Open).");
            // Fail-open: no tirar el tráfico completo si Redis sufre una caída transitoria
            return new RateLimitResult(true, 1, 0);
        }
    }
}
