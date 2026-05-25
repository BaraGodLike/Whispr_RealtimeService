using Application.Abstractions;
using Application.Exceptions;

namespace Application.UseCases;

public sealed class AcknowledgeMessagesBatchUseCase(IRelayClient relayClient)
{
    public async Task<int> ExecuteAsync(
        IReadOnlyCollection<string> messageIds,
        CancellationToken cancellationToken)
    {
        if (messageIds.Count == 0 || messageIds.Any(string.IsNullOrWhiteSpace))
        {
            throw new ClientRequestValidationException("At least one message id is required.");
        }

        return await relayClient.AckMessagesBatchAsync(messageIds, cancellationToken);
    }
}
