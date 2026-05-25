using Application.Abstractions;
using Application.Exceptions;
using Application.Options;
using Microsoft.Extensions.Options;

namespace Application.UseCases;

public sealed class ResumePendingMessagesUseCase(
    IRelayClient relayClient,
    IOptions<RealtimeServiceOptions> realtimeOptions)
{
    public async Task<GetPendingMessagesResult> ExecuteAsync(
        IReadOnlyCollection<string> mailboxes,
        int limit,
        CancellationToken cancellationToken)
    {
        if (mailboxes.Count == 0)
        {
            throw new ClientRequestValidationException("No active mailboxes registered for this connection.");
        }

        if (limit < 1 || limit > realtimeOptions.Value.MaxResumeBatchSize)
        {
            throw new ClientRequestValidationException("Resume limit is invalid.");
        }

        return await relayClient.GetPendingMessagesAsync(mailboxes, limit, cancellationToken);
    }
}
