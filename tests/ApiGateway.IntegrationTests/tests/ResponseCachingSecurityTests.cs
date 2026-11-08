namespace ApiGateway.IntegrationTests.tests;

using System.Net;
using System.Net.Http.Headers;
using ApiGateway.Configuration;
using Microsoft.Extensions.DependencyInjection;

[Collection("GatewayIntegrationTests")]
public class ResponseCachingSecurityTests(ApiGatewayWebApplicationFactory factory) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await factory.FlushRedisAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetOrders_WhenCached_ShouldNotLeakUserADataToUserB_AndIncludeVaryAuthorization()
    {
        // Simular que alguien agrega la ruta privada /api/v1/orders a las rutas cacheadas
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

        var userAId = $"user-a-{Guid.NewGuid()}";
        var userBId = $"user-b-{Guid.NewGuid()}";

        var clientUserA = customFactory.CreateClient();
        clientUserA.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtTokenGenerator.GenerateToken(userAId, tier: "Premium"));

        var clientUserB = customFactory.CreateClient();
        clientUserB.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtTokenGenerator.GenerateToken(userBId, tier: "Premium"));

        // 1. Usuario A consulta sus órdenes por primera vez -> MISS
        var responseA1 = await clientUserA.GetAsync("/api/v1/orders");
        var bodyA1 = await responseA1.Content.ReadAsStringAsync();

        // 2. Usuario A consulta sus órdenes por segunda vez -> HIT (su propio caché aislado)
        var responseA2 = await clientUserA.GetAsync("/api/v1/orders");
        var bodyA2 = await responseA2.Content.ReadAsStringAsync();

        // 3. Usuario B consulta sus órdenes -> Debe recibir MISS y su propio cuerpo (nunca el de A)
        var responseB = await clientUserB.GetAsync("/api/v1/orders");
        var bodyB = await responseB.Content.ReadAsStringAsync();

        // Assert Usuario A (1ra y 2da llamada)
        Assert.Equal(HttpStatusCode.OK, responseA1.StatusCode);
        Assert.Equal("MISS", responseA1.Headers.GetValues("X-Cache").First());
        Assert.Contains("Authorization", responseA1.Headers.Vary);
        Assert.Contains($"Order-Secret-Of-{userAId}", bodyA1);

        Assert.Equal(HttpStatusCode.OK, responseA2.StatusCode);
        Assert.Equal("HIT", responseA2.Headers.GetValues("X-Cache").First());
        Assert.Contains("Authorization", responseA2.Headers.Vary);
        Assert.Contains($"Order-Secret-Of-{userAId}", bodyA2);

        // Assert Usuario B (Aislamiento Zero-Trust)
        Assert.Equal(HttpStatusCode.OK, responseB.StatusCode);
        Assert.Equal("MISS", responseB.Headers.GetValues("X-Cache").First());
        Assert.Contains("Authorization", responseB.Headers.Vary);
        Assert.DoesNotContain($"Order-Secret-Of-{userAId}", bodyB);
        Assert.Contains($"Order-Secret-Of-{userBId}", bodyB);
    }

    [Fact]
    public async Task GetPublicRoute_WhenCalledByDifferentUsers_ShouldReturnCacheHit()
    {
        var clientUserA = factory.CreateClient();
        clientUserA.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtTokenGenerator.GenerateToken($"user-pub-a-{Guid.NewGuid()}"));

        var clientUserB = factory.CreateClient();
        clientUserB.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtTokenGenerator.GenerateToken($"user-pub-b-{Guid.NewGuid()}"));

        // Usuario A calienta el caché de la ruta pública /api/v1/catalog/products -> MISS
        var responseA = await clientUserA.GetAsync("/api/v1/catalog/products");
        // Usuario B consulta la misma ruta pública -> HIT compartido
        var responseB = await clientUserB.GetAsync("/api/v1/catalog/products");

        Assert.Equal(HttpStatusCode.OK, responseA.StatusCode);
        Assert.Equal("MISS", responseA.Headers.GetValues("X-Cache").First());

        Assert.Equal(HttpStatusCode.OK, responseB.StatusCode);
        Assert.Equal("HIT", responseB.Headers.GetValues("X-Cache").First());
    }

    [Fact]
    public async Task GetPrivateRoute_WhenTokenHasNoSubClaim_ShouldBypassCache()
    {
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

        var client = customFactory.CreateClient();
        // Token válido en firma pero sin el claim "sub" (userId = null)
        var tokenWithoutSub = TestJwtTokenGenerator.GenerateToken(userId: null, tier: "Premium");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenWithoutSub);

        var response1 = await client.GetAsync("/api/v1/orders");
        var response2 = await client.GetAsync("/api/v1/orders");

        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);

        // Al no tener "sub" en una ruta no pública, el middleware hace bypass total del caché (no añade X-Cache)
        Assert.False(response1.Headers.Contains("X-Cache"));
        Assert.False(response2.Headers.Contains("X-Cache"));
    }

    [Fact]
    public async Task GetCatalog_WithDifferentQueryOrderAndKeyCase_ShouldReturnCacheHit()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtTokenGenerator.GenerateToken($"user-query-{Guid.NewGuid()}"));

        var response1 = await client.GetAsync("/api/v1/catalog/products?b=2&a=1");
        var response2 = await client.GetAsync("/api/v1/catalog/products?A=1&B=2");

        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal("MISS", response1.Headers.GetValues("X-Cache").First());

        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        Assert.Equal("HIT", response2.Headers.GetValues("X-Cache").First());
    }

    [Fact]
    public async Task GetCatalog_WithDifferentQueryValueCase_ShouldPreserveValueCaseAndNotCollide()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtTokenGenerator.GenerateToken($"user-valcase-{Guid.NewGuid()}"));

        // ?id=AbC y ?id=abc tienen valores distintos (case-sensitive) y NO deben colisionar en la misma clave de caché
        var responseUpperMixed = await client.GetAsync("/api/v1/catalog/products?id=AbC");
        var bodyUpperMixed = await responseUpperMixed.Content.ReadAsStringAsync();

        var responseLower = await client.GetAsync("/api/v1/catalog/products?id=abc");
        var bodyLower = await responseLower.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, responseUpperMixed.StatusCode);
        Assert.Equal("MISS", responseUpperMixed.Headers.GetValues("X-Cache").First());
        Assert.Contains("?id=AbC", bodyUpperMixed);

        Assert.Equal(HttpStatusCode.OK, responseLower.StatusCode);
        Assert.Equal("MISS", responseLower.Headers.GetValues("X-Cache").First());
        Assert.Contains("?id=abc", bodyLower);
    }
}
