using Microsoft.IdentityModel.Tokens;

namespace ApiGateway.Services;

public interface IJwksKeyResolverService
{
    IEnumerable<SecurityKey> ResolveSigningKeys(
        string token,
        SecurityToken securityToken,
        string kid,
        TokenValidationParameters validationParameters);
}

/// <summary>
/// Resuelve y cachea en memoria las claves públicas RSA (RS256) expuestas por AuthService
/// en su endpoint estándar RFC 7517 (/.well-known/jwks.json).
/// Soporta rotación automática de claves (recarga bajo demanda cuando aparece un 'kid' desconocido).
/// </summary>
public class JwksKeyResolverService(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<JwksKeyResolverService> logger) : IJwksKeyResolverService
{
    private readonly object _syncLock = new();
    private IList<SecurityKey> _cachedKeys = [];
    private DateTime _lastFetchUtc = DateTime.MinValue;

    public IEnumerable<SecurityKey> ResolveSigningKeys(
        string token,
        SecurityToken securityToken,
        string kid,
        TokenValidationParameters validationParameters)
    {
        var currentKeys = _cachedKeys;
        if (currentKeys.Count > 0 && (string.IsNullOrEmpty(kid) || currentKeys.Any(k => k.KeyId == kid)))
        {
            return currentKeys;
        }

        lock (_syncLock)
        {
            if (_cachedKeys.Count > 0 && (string.IsNullOrEmpty(kid) || _cachedKeys.Any(k => k.KeyId == kid)))
            {
                return _cachedKeys;
            }

            // Evitar tormenta de peticiones si llegan tokens con un kid inválido
            if ((DateTime.UtcNow - _lastFetchUtc) < TimeSpan.FromSeconds(5) && _cachedKeys.Count > 0)
            {
                return _cachedKeys;
            }

            var jwksUri = configuration["Jwt:JwksUri"]
                ?? "http://auth-service:8085/api/v1/authentication/.well-known/jwks.json";

            try
            {
                logger.LogInformation("Descargando claves públicas JWKS (RS256) desde {JwksUri} para kid '{Kid}'...", jwksUri, kid);
                using var client = httpClientFactory.CreateClient("JwksClient");
                client.Timeout = TimeSpan.FromSeconds(5);

                var jwksJson = client.GetStringAsync(jwksUri).GetAwaiter().GetResult();
                var jwks = new JsonWebKeySet(jwksJson);
                _cachedKeys = jwks.GetSigningKeys();
                _lastFetchUtc = DateTime.UtcNow;

                logger.LogInformation("JWKS cargado exitosamente. Claves públicas RSA en caché: {Count}", _cachedKeys.Count);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error al obtener JWKS desde {JwksUri}", jwksUri);
            }

            return _cachedKeys;
        }
    }
}
