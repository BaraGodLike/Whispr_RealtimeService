using Domain.Connections;

namespace Application.Abstractions;

public interface IConnectionLeaseStore
{
    Task<RegisterConnectionLeaseResult> RegisterAuthenticatedConnectionAsync(
        string nodeId,
        string connectionId,
        IReadOnlyCollection<string> mailboxes,
        TimeSpan ttl,
        CancellationToken cancellationToken);

    Task<RefreshConnectionLeaseResult> RefreshConnectionAsync(
        string nodeId,
        string connectionId,
        TimeSpan ttl,
        CancellationToken cancellationToken);

    Task<ConnectionLease?> GetLeaseByMailboxAsync(string mailbox, CancellationToken cancellationToken);

    Task ReleaseConnectionAsync(string nodeId, string connectionId, CancellationToken cancellationToken);
}

public sealed record RegisterConnectionLeaseResult(
    int RegisteredMailboxCount,
    IReadOnlyCollection<string> DisplacedLocalConnectionIds);

public sealed record RefreshConnectionLeaseResult(
    bool LeaseLost,
    IReadOnlyList<string> Mailboxes);
