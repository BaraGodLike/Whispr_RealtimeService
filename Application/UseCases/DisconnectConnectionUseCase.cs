using Application.Abstractions;
using Application.Options;
using Microsoft.Extensions.Options;

namespace Application.UseCases;

public sealed class DisconnectConnectionUseCase(
    IConnectionLeaseStore connectionLeaseStore,
    IOptions<RealtimeServiceOptions> realtimeOptions)
{
    public Task ExecuteAsync(string connectionId, CancellationToken cancellationToken)
    {
        return connectionLeaseStore.ReleaseConnectionAsync(
            realtimeOptions.Value.NodeId,
            connectionId,
            cancellationToken);
    }
}
