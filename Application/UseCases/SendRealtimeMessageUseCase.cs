using Application.Abstractions;
using Application.Exceptions;
using Application.Options;
using Microsoft.Extensions.Options;

namespace Application.UseCases;

public sealed class SendRealtimeMessageUseCase(
    IRelayClient relayClient,
    IOptions<RealtimeServiceOptions> realtimeOptions)
{
    public async Task<bool> ExecuteAsync(
        SendRealtimeMessageCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.MessageId) ||
            string.IsNullOrWhiteSpace(command.DestinationMailbox))
        {
            throw new ClientRequestValidationException("Message request is invalid.");
        }

        if (command.Payload.Length == 0 || command.Payload.Length > realtimeOptions.Value.MaxPayloadBytes)
        {
            throw new ClientRequestValidationException("Message payload is invalid.");
        }

        return await relayClient.EnqueueMessageAsync(
            command.MessageId,
            command.DestinationMailbox,
            command.Payload,
            cancellationToken);
    }
}

public sealed record SendRealtimeMessageCommand(
    string MessageId,
    string DestinationMailbox,
    byte[] Payload);
