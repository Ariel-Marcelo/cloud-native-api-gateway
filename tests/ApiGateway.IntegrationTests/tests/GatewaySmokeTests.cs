namespace ApiGateway.IntegrationTests;
using System.Net;

public class GatewaySmokeTests(ApiGatewayWebApplicationFactory factory)
    : IClassFixture<ApiGatewayWebApplicationFactory>
{
    [Fact]
    public async Task Healthz_ShouldReturn200Ok()
    {
        // Arrange
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/healthz");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}