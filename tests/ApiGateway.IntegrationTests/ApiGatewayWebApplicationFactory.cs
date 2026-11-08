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
    private static readonly string _redisImage =
        Environment.GetEnvironmentVariable("TEST_REDIS_IMAGE") ?? "redis:7-alpine";
    
    private readonly RedisContainer _redisContainer = new RedisBuilder(_redisImage)
        .Build();
    
    private IConnectionMultiplexer? _redisMultiplexer;
    
    public async Task InitializeAsync()
    {
        await _redisContainer.StartAsync();
        
        // Conectar de forma ASÍNCRONA antes de recibir tráfico HTTP para evitar ThreadPool Starvation en CI (2 vCPUs)
        var redisOptions = ConfigurationOptions.Parse(_redisContainer.GetConnectionString());
        redisOptions.AbortOnConnectFail = false;
        redisOptions.ConnectRetry = 5;
        redisOptions.ConnectTimeout = 10000;
        _redisMultiplexer = await ConnectionMultiplexer.ConnectAsync(redisOptions);
    }
    
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            // Quitamos el IConnectionMultiplexer que apuntaba a localhost:6379 en Program.cs
            services.RemoveAll<IConnectionMultiplexer>();
            
            // Inyectamos la instancia ya conectada (cero bloqueo en el contenedor de DI)
            services.AddSingleton<IConnectionMultiplexer>(_redisMultiplexer!);
            
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
        if (_redisMultiplexer is not null)
        {
            await _redisMultiplexer.DisposeAsync();
        }
        await _redisContainer.DisposeAsync();
        await base.DisposeAsync();
    }
}