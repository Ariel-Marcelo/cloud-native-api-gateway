namespace ApiGateway.IntegrationTests.tests;

using System.Net;
using System.Net.Http.Headers;
using ApiGateway.Configuration;
using Microsoft.Extensions.DependencyInjection;

public class ResponseCachingSecurityTests(ApiGatewayWebApplicationFactory factory)
    : IClassFixture<ApiGatewayWebApplicationFactory>
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
}