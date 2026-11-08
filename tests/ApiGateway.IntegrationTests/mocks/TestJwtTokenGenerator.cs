namespace ApiGateway.IntegrationTests;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using ApiGateway.Services;
using Microsoft.IdentityModel.Tokens;

public static class TestJwtTokenGenerator
{
    private static readonly RSA Rsa = RSA.Create(2048);
    public static readonly RsaSecurityKey SigningKey = new(Rsa) { KeyId = "test-key-id" };

    public const string Issuer = "apigateway-auth";
    public const string Audience = "apigateway-clients";

    public static string GenerateToken(
        string? userId,
        string username = "testuser",
        string role = "User",
        string tier = "Free",
        string? issuer = null,
        string? audience = null,
        DateTime? notBefore = null,
        DateTime? expires = null,
        SecurityKey? customSigningKey = null)
    {
        var keyToUse = customSigningKey ?? SigningKey;
        var credentials = new SigningCredentials(keyToUse, SecurityAlgorithms.RsaSha256);

        var claims = new List<Claim>
        {
            new("username", username),
            new("role", role),
            new("tier", tier)
        };

        if (!string.IsNullOrWhiteSpace(userId))
        {
            claims.Add(new Claim("sub", userId));
        }

        var token = new JwtSecurityToken(
            issuer: issuer ?? Issuer,
            audience: audience ?? Audience,
            claims: claims,
            notBefore: notBefore ?? DateTime.UtcNow.AddMinutes(-5),
            expires: expires ?? DateTime.UtcNow.AddMinutes(30),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static string GenerateTokenWithDifferentKey(string userId)
    {
        using var differentRsa = RSA.Create(2048);
        var differentKey = new RsaSecurityKey(differentRsa.ExportParameters(true))
        {
            KeyId = "untrusted-key-id"
        };

        return GenerateToken(userId, customSigningKey: differentKey);
    }
}

public class FakeJwksKeyResolverService : IJwksKeyResolverService
{
    public IEnumerable<SecurityKey> ResolveSigningKeys(
        string token, SecurityToken securityToken, string kid, TokenValidationParameters validationParameters)
    {
        return [TestJwtTokenGenerator.SigningKey];
    }
}
