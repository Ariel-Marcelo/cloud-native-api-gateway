using ApiGateway.Services;
using Yarp.ReverseProxy.Forwarder;

namespace ApiGateway.IntegrationTests;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;
using Testcontainers.Redis;


public class ApiGatewayWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private static readonly string RedisImage =
        Environment.GetEnvironmentVariable("TEST_REDIS_IMAGE") ?? "redis:7-alpine";
    
    private readonly RedisContainer _redisContainer = new RedisBuilder()
        .WithImage(RedisImage)
        .Build();
    
    public async Task InitializeAsync()
    {
        await _redisContainer.StartAsync();
    }
    
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            // Quitamos el IConnectionMultiplexer que apuntaba a localhost:6379 en Program.cs
            services.RemoveAll<IConnectionMultiplexer>();

            // Registramos uno nuevo conectado al contenedor efímero de Testcontainers
            services.AddSingleton<IConnectionMultiplexer>(_ =>
                ConnectionMultiplexer.Connect(_redisContainer.GetConnectionString()));
            
            // Reemplazar el resolvedor JWKS para no llamar por red a auth-service
            services.RemoveAll<IJwksKeyResolverService>();
            services.AddSingleton<IJwksKeyResolverService, FakeJwksKeyResolverService>();
            
            // Reemplazar el cliente HTTP de YARP para simular los microservicios downstream en memoria
            services.RemoveAll<IForwarderHttpClientFactory>();
            services.AddSingleton<IForwarderHttpClientFactory, MockDownstreamHttpClientFactory>();
        });
    }

    // DESPUÉS de terminar los tests
    public new async Task DisposeAsync()
    {
        await _redisContainer.DisposeAsync();
        await base.DisposeAsync();
    }
}