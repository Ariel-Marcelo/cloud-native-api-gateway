namespace ApiGateway.Configuration;

public class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public bool Enabled { get; set; } = true;
    public string DefaultTier { get; set; } = "Free";
    public Dictionary<string, TierQuota> Tiers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class TierQuota
{
    public int PermitLimit { get; set; } = 5;
    public int WindowSeconds { get; set; } = 10;
}
