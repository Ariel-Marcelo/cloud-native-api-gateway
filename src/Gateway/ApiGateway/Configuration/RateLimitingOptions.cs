namespace ApiGateway.Configuration;

public class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public bool Enabled { get; set; } = true;
    public int PermitLimit { get; set; } = 5;
    public int WindowSeconds { get; set; } = 10;
}
