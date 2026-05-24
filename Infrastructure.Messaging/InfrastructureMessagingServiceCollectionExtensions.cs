using Infrastructure.Messaging.HostedServices;
using Infrastructure.Messaging.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Messaging;

public static class InfrastructureMessagingServiceCollectionExtensions
{
    public static IServiceCollection AddMessagingInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<KafkaConsumerOptions>()
            .Bind(configuration.GetSection(KafkaConsumerOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.BootstrapServers), "Kafka bootstrap servers are required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Topic), "Kafka topic is required.")
            .ValidateOnStart();

        services.AddHostedService<KafkaMessageEnqueuedConsumerService>();

        return services;
    }
}
