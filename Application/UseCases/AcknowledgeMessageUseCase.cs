using Application.Abstractions;
using Application.Exceptions;

namespace Application.UseCases;

public sealed class AcknowledgeMessageUseCase(IRelayClient relayClient)
{
    public async Task<bool> ExecuteAsync(string messageId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            throw new ClientRequestValidationException("Message id is required.");
        }

        return await relayClient.AckMessageAsync(messageId, cancellationToken);
    }
}
