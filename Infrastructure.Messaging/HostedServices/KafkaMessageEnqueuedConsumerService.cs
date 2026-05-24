using Application.Abstractions;
using Application.Logging;
using Application.Options;
using Confluent.Kafka;
using Domain.Messages;
using Infrastructure.Messaging.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RelayService.Protos;

namespace Infrastructure.Messaging.HostedServices;

internal sealed class KafkaMessageEnqueuedConsumerService(
    IServiceScopeFactory serviceScopeFactory,
    IOptions<KafkaConsumerOptions> kafkaOptions,
    IOptions<RealtimeServiceOptions> realtimeOptions,
    IRealtimeLogScopeFactory logScopeFactory,
    ILogger<KafkaMessageEnqueuedConsumerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var logScope = logScopeFactory.BeginScope(logger);
        var options = kafkaOptions.Value;
        using var consumer = CreateConsumer(options, realtimeOptions.Value.NodeId);
        consumer.Subscribe(options.Topic);

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<Ignore, byte[]>? consumeResult = null;

            try
            {
                consumeResult = consumer.Consume(stoppingToken);
                if (consumeResult?.Message?.Value is null)
                {
                    continue;
                }

                var message = MessageEnqueuedEvent.Parser.ParseFrom(consumeResult.Message.Value);
                using var scope = serviceScopeFactory.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<IMessageEnqueuedHandler>();

                await handler.HandleAsync(
                    new MessageEnqueuedNotification(message.MsgId, message.DestMailbox),
                    stoppingToken);

                consumer.Commit(consumeResult);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "Kafka message processing failed. ExceptionType={ExceptionType}",
                    exception.GetType().Name);

                if (consumeResult is not null)
                {
                    await Task.Delay(options.RetryDelay, stoppingToken);
                    consumer.Seek(consumeResult.TopicPartitionOffset);
                }
            }
        }
    }

    private static IConsumer<Ignore, byte[]> CreateConsumer(KafkaConsumerOptions options, string nodeId)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = options.BootstrapServers,
            GroupId = $"{options.GroupIdPrefix}-{Sanitize(nodeId)}",
            EnableAutoCommit = false,
            AutoOffsetReset = options.AutoOffsetReset,
            AllowAutoCreateTopics = false
        };

        return new ConsumerBuilder<Ignore, byte[]>(config).Build();
    }

    private static string Sanitize(string value)
    {
        var buffer = value
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray();

        return new string(buffer);
    }
}
