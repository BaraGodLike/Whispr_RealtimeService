namespace Application.Abstractions;

public interface IMailboxAuthClient
{
    Task<IReadOnlyList<string>> CompleteRealtimeAuthAsync(
        string userId,
        string nonce,
        string alg,
        byte[] signature,
        CancellationToken cancellationToken);
}
