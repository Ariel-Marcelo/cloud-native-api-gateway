namespace ApiGateway.IntegrationTests;

using System.Net;
using System.Text;
using System.Text.Json;
using Yarp.ReverseProxy.Forwarder;

public class MockDownstreamHttpClientFactory : IForwarderHttpClientFactory
{
    public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context)
    {
        return new HttpMessageInvoker(new MockDownstreamHandler(), disposeHandler: false);
    }

    private class MockDownstreamHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            var query = request.RequestUri?.Query ?? string.Empty;
            
            if (path.StartsWith("/api/v1/orders", StringComparison.OrdinalIgnoreCase))
            {
                // Leemos el header X-User-Id que el Gateway inyectó desde el JWT
                var userId = request.Headers.TryGetValues("X-User-Id", out var values)
                    ? values.FirstOrDefault() ?? "anonymous"
                    : "anonymous";

                var payload = JsonSerializer.Serialize(new
                {
                    ownerId = userId,
                    orders = new[] { $"Order-Secret-Of-{userId}" },
                    query = query
                });

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                });
            }

            
            if (path.StartsWith("/api/v1/products", StringComparison.OrdinalIgnoreCase))
            {
                var payload = JsonSerializer.Serialize(new
                {
                    products = new[] { "Laptop", "Mouse" },
                    filter = query
                });

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}