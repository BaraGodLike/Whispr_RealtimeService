using Application.Abstractions;
using Application.Logging;
using Application.UseCases;
using Contracts.Protocol;
using Microsoft.Extensions.Logging;
using Realtime.Services.Connections;

namespace Realtime.Services.Messaging;

public sealed class MessageEnqueuedHandler(
    DeliverIncomingMessageUseCase deliverIncomingMessageUseCase,
    IWebSocketConnectionRegistry connectionRegistry,
    IRealtimeLogScopeFactory logScopeFactory,
    ILogger<MessageEnqueuedHandler> logger) : IMessageEnqueuedHandler
{
    public async Task HandleAsync(Domain.Messages.MessageEnqueuedNotification notification, CancellationToken cancellationToken)
    {
        using var logScope = logScopeFactory.BeginScope(logger);
        var result = await deliverIncomingMessageUseCase.ExecuteAsync(notification, cancellationToken);
        if (!result.ShouldDeliver || result.ConnectionId is null || result.MessageId is null || result.Payload is null)
        {
            if (result.SkipReason == DeliverySkipReason.MessageNotFound)
            {
                logger.LogWarning("Relay returned message not found during delivery.");
            }

            return;
        }

        var payload = RealtimeJson.Serialize(new ServerEnvelope<IncomingMessageMessage>(
            RealtimeMessageTypes.IncomingMessage,
            new IncomingMessageMessage(result.MessageId, Convert.ToBase64String(result.Payload))));

        await connectionRegistry.SendAsync(result.ConnectionId, payload, cancellationToken);
    }
}
