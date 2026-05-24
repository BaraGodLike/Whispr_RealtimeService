using Application.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<AuthenticateConnectionUseCase>();
        services.AddScoped<SendRealtimeMessageUseCase>();
        services.AddScoped<AcknowledgeMessageUseCase>();
        services.AddScoped<AcknowledgeMessagesBatchUseCase>();
        services.AddScoped<ResumePendingMessagesUseCase>();
        services.AddScoped<DeliverIncomingMessageUseCase>();
        services.AddScoped<RefreshConnectionLeaseUseCase>();
        services.AddScoped<DisconnectConnectionUseCase>();

        return services;
    }
}
