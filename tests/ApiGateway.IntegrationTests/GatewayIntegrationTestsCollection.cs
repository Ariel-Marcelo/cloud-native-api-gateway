namespace ApiGateway.IntegrationTests;

[CollectionDefinition("GatewayIntegrationTests")]
public class GatewayIntegrationTestsCollection : ICollectionFixture<ApiGatewayWebApplicationFactory>
{
    // Esta clase no tiene código ni se instancia directamente.
    // Su único propósito es servir de ancla para que xUnit comparta una única instancia
    // de ApiGatewayWebApplicationFactory (y un solo contenedor de Redis) entre todas las clases de test.
}
