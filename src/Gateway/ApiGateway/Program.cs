using ApiGateway.Configuration;
using ApiGateway.Middlewares;
using ApiGateway.Services;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Configuración de Rate Limiting
builder.Services.Configure<RateLimitingOptions>(
    builder.Configuration.GetSection(RateLimitingOptions.SectionName));

// Configuración de Redis con reconexión resiliente
var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
var redisConfiguration = ConfigurationOptions.Parse(redisConnectionString);
redisConfiguration.AbortOnConnectFail = false; // Permite iniciar mientras Redis termina de arrancar en contenedores
redisConfiguration.ConnectRetry = 5;
redisConfiguration.ConnectTimeout = 5000;

builder.Services.AddSingleton<IConnectionMultiplexer>(_ => 
    ConnectionMultiplexer.Connect(redisConfiguration));

builder.Services.AddSingleton<IRedisRateLimiterService, RedisRateLimiterService>();

// Configuración de YARP Reverse Proxy
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddHealthChecks();

var app = builder.Build();

// 1. Middleware de Correlación y Trazabilidad (primero en la canalización)
app.UseMiddleware<CorrelationIdMiddleware>();

// 2. Middleware de Control de Tráfico Distribuido (Rate Limiting con Redis)
app.UseMiddleware<DistributedRateLimitingMiddleware>();

// Endpoints propios del Gateway
app.MapGet("/", () => Results.Ok(new
{
    Gateway = "ApiGateway (YARP + Redis)",
    Version = "1.0.0",
    Status = "Online",
    Routes = new[]
    {
        "/catalog/products (Proxy -> CatalogService)",
        "/healthz (Health Check del Gateway)"
    }
}));

app.MapGet("/healthz", () => Results.Ok(new
{
    Status = "Healthy",
    Service = "ApiGateway",
    Machine = Environment.MachineName,
    Timestamp = DateTime.UtcNow
}));

// 3. Enrutamiento del Reverse Proxy (YARP) hacia microservicios downstream
app.MapReverseProxy();

app.Run();
