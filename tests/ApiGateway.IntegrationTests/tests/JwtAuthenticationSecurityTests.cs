namespace ApiGateway.IntegrationTests.tests;

using System.Net;
using System.Net.Http.Headers;
using ApiGateway.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

[Collection("GatewayIntegrationTests")]
public class JwtAuthenticationSecurityTests(ApiGatewayWebApplicationFactory factory)
{
    [Fact]
    public async Task Request_WithTokenSignedByUntrustedRsaKey_ShouldReturn401Unauthorized()
    {
        var client = factory.CreateClient();
        var forgedToken = TestJwtTokenGenerator.GenerateTokenWithDifferentKey($"user-{Guid.NewGuid()}");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", forgedToken);

        var response = await client.GetAsync("/api/v1/orders");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Request_WithExpiredToken_ShouldReturn401Unauthorized()
    {
        var client = factory.CreateClient();
        var expiredToken = TestJwtTokenGenerator.GenerateToken(
            userId: $"user-{Guid.NewGuid()}",
            notBefore: DateTime.UtcNow.AddMinutes(-10),
            expires: DateTime.UtcNow.AddMinutes(-1));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", expiredToken);

        var response = await client.GetAsync("/api/v1/orders");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("wrong-issuer", TestJwtTokenGenerator.Audience)]
    [InlineData(TestJwtTokenGenerator.Issuer, "wrong-audience")]
    public async Task Request_WithInvalidIssuerOrAudience_ShouldReturn401Unauthorized(string issuer, string audience)
    {
        var client = factory.CreateClient();
        var invalidToken = TestJwtTokenGenerator.GenerateToken(
            userId: $"user-{Guid.NewGuid()}",
            issuer: issuer,
            audience: audience);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", invalidToken);

        var response = await client.GetAsync("/api/v1/orders");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Request_WhenJwksEndpointIsUnreachable_ShouldFailClosedWith401Unauthorized()
    {
        // Usamos el JwksKeyResolverService REAL apuntando a un endpoint JWKS inalcanzable
        // para verificar que el Gateway falla de forma segura (Fail-Closed -> 401) sin lanzar 500
        using var customFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:JwksUri", "http://127.0.0.1:1/unreachable-jwks.json");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IJwksKeyResolverService>();
                services.AddSingleton<IJwksKeyResolverService, JwksKeyResolverService>();
            });
        });

        var client = customFactory.CreateClient();
        var validLookingToken = TestJwtTokenGenerator.GenerateToken($"user-{Guid.NewGuid()}");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", validLookingToken);

        var response = await client.GetAsync("/api/v1/orders");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
