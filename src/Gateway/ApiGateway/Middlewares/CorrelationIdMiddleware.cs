namespace ApiGateway.Middlewares;

public class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    private const string _correlationIdHeader = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        // Obtener o generar un nuevo Correlation ID
        var correlationId = context.Request.Headers[_correlationIdHeader].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Guid.NewGuid().ToString("N");
            context.Request.Headers[_correlationIdHeader] = correlationId;
        }

        context.TraceIdentifier = correlationId;

        // Inyectar en los encabezados de respuesta
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[_correlationIdHeader] = correlationId;
            return Task.CompletedTask;
        });

        // Crear un scope de logging estructurado para que todos los logs de la petición incluyan el CorrelationId
        using (logger.BeginScope("CorrelationId:{CorrelationId}", correlationId))
        {
            logger.LogInformation("Solicitud entrante: {Method} {Path}", context.Request.Method, context.Request.Path);
            await next(context);
            logger.LogInformation("Solicitud finalizada con código de estado: {StatusCode}", context.Response.StatusCode);
        }
    }
}
