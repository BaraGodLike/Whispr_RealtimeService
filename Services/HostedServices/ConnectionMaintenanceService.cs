using System.Net.WebSockets;
using Application.Logging;
using Application.Options;
using Application.UseCases;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Realtime.Services.Connections;

namespace Realtime.Services.HostedServices;

public sealed class ConnectionMaintenanceService(
    IServiceScopeFactory serviceScopeFactory,
    IWebSocketConnectionRegistry connectionRegistry,
    IOptions<RealtimeServiceOptions> realtimeOptions,
    IRealtimeLogScopeFactory logScopeFactory,
    ILogger<ConnectionMaintenanceService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var logScope = logScopeFactory.BeginScope(logger);
        using var timer = new PeriodicTimer(realtimeOptions.Value.HeartbeatCheckInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var connection in connectionRegistry.Snapshot())
            {
                try
                {
                    if (!connection.IsAuthenticated)
                    {
                        await CloseTimedOutUnauthenticatedConnectionAsync(connection, stoppingToken);
                        continue;
                    }

                    await RefreshAuthenticatedLeaseAsync(connection, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    logger.LogError(
                        "Connection maintenance failed. ExceptionType={ExceptionType}",
                        exception.GetType().Name);
                }
            }
        }
    }

    private async Task CloseTimedOutUnauthenticatedConnectionAsync(
        ConnectionContext connection,
        CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow - connection.ConnectedAt < realtimeOptions.Value.AuthTimeout)
        {
            return;
        }

        await connectionRegistry.CloseAsync(
            connection.ConnectionId,
            WebSocketCloseStatus.PolicyViolation,
            "Authentication timeout.",
            cancellationToken);
    }

    private async Task RefreshAuthenticatedLeaseAsync(
        ConnectionContext connection,
        CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow - connection.LastLeaseRefreshAt < realtimeOptions.Value.LeaseRefreshInterval)
        {
            return;
        }

        using var scope = serviceScopeFactory.CreateScope();
        var refreshUseCase = scope.ServiceProvider.GetRequiredService<RefreshConnectionLeaseUseCase>();
        var refreshResult = await refreshUseCase.ExecuteAsync(connection.ConnectionId, cancellationToken);

        if (refreshResult.LeaseLost)
        {
            await connectionRegistry.CloseAsync(
                connection.ConnectionId,
                WebSocketCloseStatus.PolicyViolation,
                "Connection superseded.",
                cancellationToken);

            return;
        }

        connection.MarkLeaseRefreshed();
    }
}
