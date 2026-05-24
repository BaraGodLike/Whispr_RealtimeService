using Application.Abstractions;
using Infrastructure.Redis.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Infrastructure.Redis;

public static class InfrastructureRedisServiceCollectionExtensions
{
    public static IServiceCollection AddRedisInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<RedisOptions>()
            .Bind(configuration.GetSection(RedisOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.Configuration), "Redis configuration is required.")
            .ValidateOnStart();

        services.AddSingleton<IConnectionMultiplexer>(serviceProvider =>
        {
            var options = serviceProvider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<RedisOptions>>()
                .Value;

            return ConnectionMultiplexer.Connect(options.Configuration);
        });

        services.AddScoped<IConnectionLeaseStore, RedisConnectionLeaseStore>();

        return services;
    }
}
