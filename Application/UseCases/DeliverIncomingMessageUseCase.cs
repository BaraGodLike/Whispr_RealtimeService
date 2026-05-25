using Application.Abstractions;
using Application.Exceptions;
using Application.Options;
using Domain.Messages;
using Microsoft.Extensions.Options;

namespace Application.UseCases;

public sealed class DeliverIncomingMessageUseCase(
    IConnectionLeaseStore connectionLeaseStore,
    IRelayClient relayClient,
    IOptions<RealtimeServiceOptions> realtimeOptions)
{
    public async Task<DeliverIncomingMessageResult> ExecuteAsync(
        MessageEnqueuedNotification notification,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(notification.MessageId) ||
            string.IsNullOrWhiteSpace(notification.DestinationMailbox))
        {
            throw new ClientRequestValidationException("Incoming event is invalid.");
        }

        var lease = await connectionLeaseStore.GetLeaseByMailboxAsync(
            notification.DestinationMailbox,
            cancellationToken);

        if (lease is null)
        {
            return DeliverIncomingMessageResult.Skip(DeliverySkipReason.Offline);
        }

        if (!string.Equals(lease.NodeId, realtimeOptions.Value.NodeId, StringComparison.Ordinal))
        {
            return DeliverIncomingMessageResult.Skip(DeliverySkipReason.ForeignNode);
        }

        var maxAttempts = Math.Max(1, realtimeOptions.Value.RelayGetMessageRetry.MaxAttempts);
        var backoff = realtimeOptions.Value.RelayGetMessageRetry.InitialBackoff;
        Exception? lastException = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var payload = await relayClient.GetMessageAsync(notification.MessageId, cancellationToken);
                return DeliverIncomingMessageResult.Deliver(lease.ConnectionId, notification.MessageId, payload);
            }
            catch (RelayMessageNotFoundException)
            {
                return DeliverIncomingMessageResult.Skip(DeliverySkipReason.MessageNotFound);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (attempt < maxAttempts)
            {
                lastException = exception;
                await Task.Delay(backoff, cancellationToken);
                backoff = TimeSpan.FromMilliseconds(backoff.TotalMilliseconds * 2);
            }
            catch (Exception exception)
            {
                lastException = exception;
                break;
            }
        }

        throw lastException ?? new InvalidOperationException("Unable to fetch message from relay.");
    }
}

public enum DeliverySkipReason
{
    None = 0,
    Offline = 1,
    ForeignNode = 2,
    MessageNotFound = 3
}

public sealed record DeliverIncomingMessageResult(
    bool ShouldDeliver,
    string? ConnectionId,
    string? MessageId,
    byte[]? Payload,
    DeliverySkipReason SkipReason)
{
    public static DeliverIncomingMessageResult Deliver(
        string connectionId,
        string messageId,
        byte[] payload) =>
        new(true, connectionId, messageId, payload, DeliverySkipReason.None);

    public static DeliverIncomingMessageResult Skip(DeliverySkipReason reason) =>
        new(false, null, null, null, reason);
}
