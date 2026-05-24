namespace Application.Options;

public sealed class RealtimeServiceOptions
{
    public const string SectionName = "Realtime";

    public string ServiceName { get; set; } = "realtime-service";

    public string NodeId { get; set; } = "realtime-1";

    public TimeSpan RedisLeaseTtl { get; set; } = TimeSpan.FromSeconds(60);

    public TimeSpan LeaseRefreshInterval { get; set; } = TimeSpan.FromSeconds(25);

    public TimeSpan HeartbeatCheckInterval { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan AuthTimeout { get; set; } = TimeSpan.FromSeconds(15);

    public int MaxPayloadBytes { get; set; } = 256 * 1024;

    public int MaxResumeBatchSize { get; set; } = 500;

    public RelayGetMessageRetryOptions RelayGetMessageRetry { get; set; } = new();
}

public sealed class RelayGetMessageRetryOptions
{
    public int MaxAttempts { get; set; } = 3;

    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromMilliseconds(200);
}
