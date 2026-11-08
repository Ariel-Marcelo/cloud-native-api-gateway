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

    public static string GenerateToken(string userId, string username = "testuser", string role = "User", string tier = "Free")
    {
        var credentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256);

        var claims = new[]
        {
            new Claim("sub", userId),
            new Claim("username", username),
            new Claim("role", role),
            new Claim("tier", tier)
        };

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
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