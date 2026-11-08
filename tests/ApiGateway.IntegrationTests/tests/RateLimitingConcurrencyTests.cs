namespace ApiGateway.IntegrationTests.tests;

using System.Net;
using System.Net.Http.Headers;

public class RateLimitingConcurrencyTests(ApiGatewayWebApplicationFactory factory)
    : IClassFixture<ApiGatewayWebApplicationFactory>
{
    [Fact]
    public async Task ConcurrentBurst_Of50Requests_ShouldAllowExactly5AndReject45With429()
    {
        // Arrange: Usuario único con Tier "Free" (PermitLimit = 5 en ventana de 10s)
        var client = factory.CreateClient();
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
