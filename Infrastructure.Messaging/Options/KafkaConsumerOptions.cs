using Confluent.Kafka;

namespace Infrastructure.Messaging.Options;

public sealed class KafkaConsumerOptions
{
    public const string SectionName = "Kafka";

    public string BootstrapServers { get; set; } = string.Empty;

    public string Topic { get; set; } = "message.enqueued";

    public string GroupIdPrefix { get; set; } = "realtime";

    public AutoOffsetReset AutoOffsetReset { get; set; } = AutoOffsetReset.Latest;

    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
}
