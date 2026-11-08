namespace ApiGateway.IntegrationTests.tests;

using System.Net;
using System.Net.Http.Headers;
using ApiGateway.Configuration;
using Microsoft.Extensions.DependencyInjection;

[Collection("GatewayIntegrationTests")]
public class ResponseCachingSecurityTests(ApiGatewayWebApplicationFactory factory)
{
    [Fact]
    public async Task GetOrders_WhenCached_ShouldNotLeakUserADataToUserB()
    {
        // Someone tries to config api/v1/orders in cache
        using var customFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.PostConfigure<ResponseCachingOptions>(options =>
                {
                    if (!options.Routes.Contains("/api/v1/orders"))
                    {
                        options.Routes.Add("/api/v1/orders");
                    }
                });
            });
        });

        var clientUserA = customFactory.CreateClient();
        clientUserA.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtTokenGenerator.GenerateToken("user-a"));

        var clientUserB = customFactory.CreateClient();
        clientUserB.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtTokenGenerator.GenerateToken("user-b"));

        // Usuario A consulta sus órdenes
        var responseA = await clientUserA.GetAsync("/api/v1/orders");
        var bodyA = await responseA.Content.ReadAsStringAsync();

        // Usuario B consulta sus órdenes
        var responseB = await clientUserB.GetAsync("/api/v1/orders");
        var bodyB = await responseB.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.OK, responseA.StatusCode);
        Assert.Contains("Order-Secret-Of-user-a", bodyA);

        Assert.Equal(HttpStatusCode.OK, responseB.StatusCode);

        Assert.DoesNotContain("Order-Secret-Of-user-a", bodyB);
        Assert.Contains("Order-Secret-Of-user-b", bodyB);
    }

    [Fact]
    public async Task GetCatalog_WithDifferentQueryOrderAndCase_ShouldReturnCacheHit()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtTokenGenerator.GenerateToken("user-query-test"));

        var response1 = await client.GetAsync("/api/v1/catalog/products?b=2&a=1");
        var response2 = await client.GetAsync("/api/v1/catalog/products?A=1&B=2");

        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal("MISS", response1.Headers.GetValues("X-Cache").First());

        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        Assert.Equal("HIT", response2.Headers.GetValues("X-Cache").First());
    }
}
