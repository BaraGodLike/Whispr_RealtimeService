using Domain.Messages;

namespace Application.Abstractions;

public interface IRelayClient
{
    Task<bool> EnqueueMessageAsync(
        string messageId,
        string destinationMailbox,
        byte[] payload,
        CancellationToken cancellationToken);

    Task<byte[]> GetMessageAsync(string messageId, CancellationToken cancellationToken);

    Task<GetPendingMessagesResult> GetPendingMessagesAsync(
        IReadOnlyCollection<string> mailboxes,
        int limit,
        CancellationToken cancellationToken);

    Task<bool> AckMessageAsync(string messageId, CancellationToken cancellationToken);

    Task<int> AckMessagesBatchAsync(
        IReadOnlyCollection<string> messageIds,
        CancellationToken cancellationToken);
}

public sealed record GetPendingMessagesResult(
    IReadOnlyList<PendingMessage> Messages,
    bool HasMore);
