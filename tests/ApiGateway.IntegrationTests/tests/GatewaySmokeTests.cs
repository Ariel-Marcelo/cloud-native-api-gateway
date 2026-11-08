namespace ApiGateway.IntegrationTests;

using System.Net;

[Collection("GatewayIntegrationTests")]
public class GatewaySmokeTests(ApiGatewayWebApplicationFactory factory)
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
