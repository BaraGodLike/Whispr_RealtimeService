using Application.Abstractions;
using Application.Exceptions;
using Domain.Messages;
using RelayService.Protos;

namespace Infrastructure.Grpc.Clients;

internal sealed class RelayGrpcClient(Relay.RelayClient relayClient) : IRelayClient
{
    public async Task<bool> EnqueueMessageAsync(
        string messageId,
        string destinationMailbox,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        var response = await relayClient.EnqueueMessageAsync(
            new EnqueueMessageRequest
            {
                MsgId = messageId,
                DestMailbox = destinationMailbox,
                Payload = Google.Protobuf.ByteString.CopyFrom(payload)
            },
            cancellationToken: cancellationToken);

        return response.Accepted;
    }

    public async Task<byte[]> GetMessageAsync(string messageId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await relayClient.GetMessageAsync(
                new GetMessageRequest { MsgId = messageId },
                cancellationToken: cancellationToken);

            return response.Payload.ToByteArray();
        }
        catch (global::Grpc.Core.RpcException rpcException) when (rpcException.StatusCode == global::Grpc.Core.StatusCode.NotFound)
        {
            throw new RelayMessageNotFoundException("Relay message was not found.");
        }
    }

    public async Task<GetPendingMessagesResult> GetPendingMessagesAsync(
        IReadOnlyCollection<string> mailboxes,
        int limit,
        CancellationToken cancellationToken)
    {
        var request = new GetPendingMessagesRequest
        {
            Limit = limit
        };

        request.MailboxIds.AddRange(mailboxes);

        var response = await relayClient.GetPendingMessagesAsync(request, cancellationToken: cancellationToken);
        var messages = response.Messages
            .Select(message => new Domain.Messages.PendingMessage(message.MsgId, message.Payload.ToByteArray()))
            .ToArray();

        return new GetPendingMessagesResult(messages, response.HasMore);
    }

    public async Task<bool> AckMessageAsync(string messageId, CancellationToken cancellationToken)
    {
        var response = await relayClient.AckMessageAsync(
            new AckMessageRequest { MsgId = messageId },
            cancellationToken: cancellationToken);

        return response.Success;
    }

    public async Task<int> AckMessagesBatchAsync(
        IReadOnlyCollection<string> messageIds,
        CancellationToken cancellationToken)
    {
        var request = new AckMessagesBatchRequest();
        request.MsgIds.AddRange(messageIds);

        var response = await relayClient.AckMessagesBatchAsync(request, cancellationToken: cancellationToken);
        return response.AckedCount;
    }
}
