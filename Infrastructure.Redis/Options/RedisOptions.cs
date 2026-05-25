namespace Infrastructure.Redis.Options;

public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    public string Configuration { get; set; } = string.Empty;
}
