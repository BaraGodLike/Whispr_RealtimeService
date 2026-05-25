using Application.Abstractions;
using Application.Options;
using Microsoft.Extensions.Options;

namespace Application.UseCases;

public sealed class RefreshConnectionLeaseUseCase(
    IConnectionLeaseStore connectionLeaseStore,
    IOptions<RealtimeServiceOptions> realtimeOptions)
{
    public Task<RefreshConnectionLeaseResult> ExecuteAsync(
        string connectionId,
        CancellationToken cancellationToken)
    {
        return connectionLeaseStore.RefreshConnectionAsync(
            realtimeOptions.Value.NodeId,
            connectionId,
            realtimeOptions.Value.RedisLeaseTtl,
            cancellationToken);
    }
}
