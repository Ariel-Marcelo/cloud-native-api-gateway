using System.Text;
using ApiGateway.Configuration;
using ApiGateway.Middlewares;
using ApiGateway.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using Yarp.ReverseProxy.Transforms;

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

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

// Configuración de YARP Reverse Proxy
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(builderContext =>
    {
        builderContext.AddRequestTransform(transformContext =>
        {
            var headers = transformContext.ProxyRequest.Headers;
            // 1. Seguridad Anti-Spoofing: borrar cualquier header X-User-* que envíe el cliente
            headers.Remove("X-User-Id");
            headers.Remove("X-User-Name");
            headers.Remove("X-User-Role");
            headers.Remove("X-User-Tier");
            // 2. Si el JWT fue validado, extraer claims e inyectarlos hacia el microservicio
            var user = transformContext.HttpContext.User;
            if (user.Identity?.IsAuthenticated == true)
            {
                var userId = user.FindFirst("sub")?.Value;
                var username = user.FindFirst("username")?.Value;
                var role = user.FindFirst("role")?.Value;
                var tier = user.FindFirst("tier")?.Value;
                if (!string.IsNullOrEmpty(userId)) headers.TryAddWithoutValidation("X-User-Id", userId);
                if (!string.IsNullOrEmpty(username)) headers.TryAddWithoutValidation("X-User-Name", username);
                if (!string.IsNullOrEmpty(role)) headers.TryAddWithoutValidation("X-User-Role", role);
                if (!string.IsNullOrEmpty(tier)) headers.TryAddWithoutValidation("X-User-Tier", tier);
            }
            return ValueTask.CompletedTask;
        });
    });

builder.Services.AddHealthChecks();


var jwtSection = builder.Configuration.GetSection("Jwt");
var secretKey = jwtSection["Secret"] ?? throw new InvalidOperationException("JWT Secret no configurado");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwtSection["Audience"],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey))
        };
    });
builder.Services.AddAuthorization();

// PIPELINE
var app = builder.Build();

// 1. Middleware de Correlación y Trazabilidad (primero en la canalización)
app.UseForwardedHeaders();
app.UseMiddleware<CorrelationIdMiddleware>();

// 2. Authentication
app.UseAuthentication();

// 3. Middleware de Control de Tráfico Distribuido (Rate Limiting con Redis)
app.UseMiddleware<DistributedRateLimitingMiddleware>();

// 4. Authorization
app.UseAuthorization();

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

// 5. Enrutamiento del Reverse Proxy (YARP) hacia microservicios downstream
app.MapReverseProxy();

app.Run();
