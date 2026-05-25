using Application.Abstractions;
using Infrastructure.Grpc.Clients;
using Infrastructure.Grpc.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Grpc;

public static class InfrastructureGrpcServiceCollectionExtensions
{
    public static IServiceCollection AddGrpcInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<MailboxGrpcOptions>()
            .Bind(configuration.GetSection(MailboxGrpcOptions.SectionName))
            .Validate(options => Uri.TryCreate(options.Address, UriKind.Absolute, out _), "Mailbox gRPC address is required.")
            .ValidateOnStart();

        services
            .AddOptions<RelayGrpcOptions>()
            .Bind(configuration.GetSection(RelayGrpcOptions.SectionName))
            .Validate(options => Uri.TryCreate(options.Address, UriKind.Absolute, out _), "Relay gRPC address is required.")
            .ValidateOnStart();

        services.AddGrpcClient<global::Services.MailboxApi.MailboxApiClient>((serviceProvider, clientOptions) =>
        {
            var options = serviceProvider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<MailboxGrpcOptions>>()
                .Value;

            clientOptions.Address = new Uri(options.Address);
        });

        services.AddGrpcClient<RelayService.Protos.Relay.RelayClient>((serviceProvider, clientOptions) =>
        {
            var options = serviceProvider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<RelayGrpcOptions>>()
                .Value;

            clientOptions.Address = new Uri(options.Address);
        });

        services.AddScoped<IMailboxAuthClient, MailboxGrpcClient>();
        services.AddScoped<IRelayClient, RelayGrpcClient>();

        return services;
    }
}
