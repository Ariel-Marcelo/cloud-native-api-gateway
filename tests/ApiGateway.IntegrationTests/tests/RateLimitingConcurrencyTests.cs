namespace ApiGateway.IntegrationTests.tests;

using System.Net;
using System.Net.Http.Headers;
using ApiGateway.Configuration;
using Microsoft.Extensions.DependencyInjection;

[Collection("GatewayIntegrationTests")]
public class RateLimitingConcurrencyTests(ApiGatewayWebApplicationFactory factory)
{
    [Fact]
    public async Task ConcurrentBurst_Of50Requests_ShouldAllowExactly5AndReject45With429()
    {
        await factory.FlushRedisAsync();

        // Configurar una ventana amplia (60s) para que la prueba de ráfaga (5/45) sea determinista
        // incluso si el runner de CI (2 vCPUs) tarda más de 10 segundos en procesar las 50 peticiones
        using var customFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.PostConfigure<RateLimitingOptions>(options =>
                {
                    options.Tiers["Free"] = new TierQuota
                    {
                        PermitLimit = 5,
                        WindowSeconds = 60
                    };
                });
            });
        });

        var client = customFactory.CreateClient();
        var userId = $"burst-user-{Guid.NewGuid()}";
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtTokenGenerator.GenerateToken(userId, tier: "Free"));

        // Act: Lanzar 50 peticiones simultáneas en paralelo contra el Gateway
        var tasks = Enumerable.Range(0, 50)
            .Select(_ => client.GetAsync("/api/v1/catalog/products"))
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        // Assert: El script Lua en Redis debe garantizar atomicidad estricta (sin Race Conditions)
        var okResponses = responses.Where(r => r.StatusCode == HttpStatusCode.OK).ToList();
        var rateLimitedResponses = responses.Where(r => r.StatusCode == HttpStatusCode.TooManyRequests).ToList();

        Assert.Equal(5, okResponses.Count);
        Assert.Equal(45, rateLimitedResponses.Count);
        Assert.All(rateLimitedResponses, response =>
        {
            Assert.True(response.Headers.Contains("Retry-After"));
            Assert.Equal("0", response.Headers.GetValues("X-RateLimit-Remaining").First());
        });
    }
}
