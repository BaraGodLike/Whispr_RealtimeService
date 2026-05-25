using Application.Abstractions;
using Application.Exceptions;

namespace Infrastructure.Grpc.Clients;

internal sealed class MailboxGrpcClient(global::Services.MailboxApi.MailboxApiClient mailboxApiClient) : IMailboxAuthClient
{
    public async Task<IReadOnlyList<string>> CompleteRealtimeAuthAsync(
        string userId,
        string nonce,
        string alg,
        byte[] signature,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await mailboxApiClient.CompleteRealtimeAuthAsync(
                new global::Services.CompleteRealtimeAuthRequest
                {
                    UserId = userId,
                    Nonce = nonce,
                    Alg = alg,
                    Signature = Google.Protobuf.ByteString.CopyFrom(signature)
                },
                cancellationToken: cancellationToken);

            return response.Mailboxes
                .Select(mailbox => mailbox.MailboxAddress)
                .Where(address => !string.IsNullOrWhiteSpace(address))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
        catch (global::Grpc.Core.RpcException rpcException) when (rpcException.StatusCode is
                   global::Grpc.Core.StatusCode.PermissionDenied or
                   global::Grpc.Core.StatusCode.Unauthenticated or
                   global::Grpc.Core.StatusCode.InvalidArgument or
                   global::Grpc.Core.StatusCode.NotFound or
                   global::Grpc.Core.StatusCode.FailedPrecondition)
        {
            throw new AuthenticationFailedException("Mailbox authentication failed.");
        }
    }
}
