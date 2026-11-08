namespace ApiGateway.Configuration;

public class ResponseCachingOptions
{
    public const string  SectionName = "ResponseCaching";
    
    public bool  Enabled { get; set; } = true;
    
    public int DefaultTtlSeconds { get; set; } = 30;

    public List<string> Routes { get; set; } = [];
    
    public List<string> PublicRoutes { get; set; } = [];
}