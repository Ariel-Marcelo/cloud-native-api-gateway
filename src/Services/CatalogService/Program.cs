using CatalogService.Middlewares;
using CatalogService.Models;

var builder = WebApplication.CreateBuilder(args);

// Configurar servicios
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Semilla inicial de productos en memoria
var products = new List<Product>
{
    new() { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = "MacBook Pro M3", Description = "Apple Silicon 16GB RAM 512GB SSD", Price = 1999.99m, Category = "Computers", Stock = 25 },
    new() { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), Name = "Sony WH-1000XM5", Description = "Wireless Noise-Canceling Headphones", Price = 349.99m, Category = "Audio", Stock = 40 },
    new() { Id = Guid.Parse("33333333-3333-3333-3333-333333333333"), Name = "Keychron Q1 Pro", Description = "Custom Mechanical Keyboard Wireless", Price = 199.00m, Category = "Accessories", Stock = 15 }
};

// Health Check
app.MapGet("/healthz", () => Results.Ok(new 
{ 
    Status = "Healthy", 
    Service = "CatalogService", 
    Machine = Environment.MachineName,
    Timestamp = DateTime.UtcNow 
}));

// Endpoints REST de Productos
app.MapGet("/api/products", (string? category, ILogger<Program> logger, HttpContext ctx) =>
{
    logger.LogInformation("Obteniendo productos");

    var result = string.IsNullOrWhiteSpace(category) 
        ? products 
        : products.Where(p => p.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList();

    return Results.Ok(result);
})
.WithName("GetProducts")
.WithOpenApi();

app.MapGet("/api/products/{id:guid}", (Guid id, ILogger<Program> logger, HttpContext ctx) =>
{
    logger.LogInformation("Buscando producto {Id}", id);

    var product = products.FirstOrDefault(p => p.Id == id);
    return product is not null ? Results.Ok(product) : Results.NotFound(new { Message = $"Producto con Id {id} no encontrado." });
})
.WithName("GetProductById")
.WithOpenApi();

app.MapPost("/api/products", (Product product, ILogger<Program> logger, HttpContext ctx) =>
{
    logger.LogInformation("[CatalogService] Creando nuevo producto '{Name}'", product.Name);

    product.Id = Guid.NewGuid();
    product.CreatedAt = DateTime.UtcNow;
    products.Add(product);

    return Results.Created($"/api/products/{product.Id}", product);
})
.WithName("CreateProduct")
.WithOpenApi();

app.Run();
