using Application.Abstractions;
using Application.Exceptions;
using Application.Options;
using Microsoft.Extensions.Options;

namespace Application.UseCases;

public sealed class AuthenticateConnectionUseCase(
    IMailboxAuthClient mailboxAuthClient,
    IConnectionLeaseStore connectionLeaseStore,
    IOptions<RealtimeServiceOptions> realtimeOptions)
{
    public async Task<AuthenticateConnectionResult> ExecuteAsync(
        AuthenticateConnectionCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.ConnectionId) ||
            string.IsNullOrWhiteSpace(command.UserId) ||
            string.IsNullOrWhiteSpace(command.Nonce) ||
            string.IsNullOrWhiteSpace(command.Alg) ||
            command.Signature.Length == 0)
        {
            throw new ClientRequestValidationException("Authentication payload is invalid.");
        }

        var mailboxes = await mailboxAuthClient.CompleteRealtimeAuthAsync(
            command.UserId,
            command.Nonce,
            command.Alg,
            command.Signature,
            cancellationToken);

        if (mailboxes.Count == 0)
        {
            throw new AuthenticationFailedException("Mailbox authentication returned no active mailboxes.");
        }

        var registration = await connectionLeaseStore.RegisterAuthenticatedConnectionAsync(
            realtimeOptions.Value.NodeId,
            command.ConnectionId,
            mailboxes,
            realtimeOptions.Value.RedisLeaseTtl,
            cancellationToken);

        return new AuthenticateConnectionResult(
            mailboxes,
            registration.DisplacedLocalConnectionIds,
            registration.RegisteredMailboxCount);
    }
}

public sealed record AuthenticateConnectionCommand(
    string ConnectionId,
    string UserId,
    string Nonce,
    string Alg,
    byte[] Signature);

public sealed record AuthenticateConnectionResult(
    IReadOnlyList<string> Mailboxes,
    IReadOnlyCollection<string> DisplacedLocalConnectionIds,
    int RegisteredMailboxCount);
