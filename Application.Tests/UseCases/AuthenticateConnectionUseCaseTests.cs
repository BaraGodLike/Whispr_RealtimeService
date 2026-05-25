using Application.Abstractions;
using Application.Options;
using Application.UseCases;
using Microsoft.Extensions.Options;

namespace Application.Tests.UseCases;

[TestClass]
public sealed class AuthenticateConnectionUseCaseTests
{
    [TestMethod]
    public async Task ExecuteAsync_RegistersConnectionAndReturnsMailboxes()
    {
        var mailboxClient = new FakeMailboxAuthClient(["mbx-1", "mbx-2"]);
        var leaseStore = new FakeConnectionLeaseStore();
        var useCase = new AuthenticateConnectionUseCase(
            mailboxClient,
            leaseStore,
            Microsoft.Extensions.Options.Options.Create(new RealtimeServiceOptions { NodeId = "realtime-1" }));

        var result = await useCase.ExecuteAsync(
            new AuthenticateConnectionCommand("conn-1", "user-1", "nonce-1", "ed25519", [1, 2, 3]),
            CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "mbx-1", "mbx-2" }, result.Mailboxes.ToArray());
        Assert.AreEqual(2, result.RegisteredMailboxCount);
        Assert.AreEqual("realtime-1", leaseStore.LastNodeId);
        Assert.AreEqual("conn-1", leaseStore.LastConnectionId);
        CollectionAssert.AreEqual(new[] { "mbx-1", "mbx-2" }, leaseStore.LastMailboxes!.ToArray());
    }

    private sealed class FakeMailboxAuthClient(IReadOnlyList<string> mailboxes) : IMailboxAuthClient
    {
        public Task<IReadOnlyList<string>> CompleteRealtimeAuthAsync(
            string userId,
            string nonce,
            string alg,
            byte[] signature,
            CancellationToken cancellationToken) =>
            Task.FromResult(mailboxes);
    }

    private sealed class FakeConnectionLeaseStore : IConnectionLeaseStore
    {
        public string? LastNodeId { get; private set; }

        public string? LastConnectionId { get; private set; }

        public IReadOnlyCollection<string>? LastMailboxes { get; private set; }

        public Task<RegisterConnectionLeaseResult> RegisterAuthenticatedConnectionAsync(
            string nodeId,
            string connectionId,
            IReadOnlyCollection<string> mailboxes,
            TimeSpan ttl,
            CancellationToken cancellationToken)
        {
            LastNodeId = nodeId;
            LastConnectionId = connectionId;
            LastMailboxes = mailboxes;

            return Task.FromResult(new RegisterConnectionLeaseResult(mailboxes.Count, Array.Empty<string>()));
        }

        public Task<RefreshConnectionLeaseResult> RefreshConnectionAsync(
            string nodeId,
            string connectionId,
            TimeSpan ttl,
            CancellationToken cancellationToken) =>
            Task.FromResult(new RefreshConnectionLeaseResult(false, Array.Empty<string>()));

        public Task<Domain.Connections.ConnectionLease?> GetLeaseByMailboxAsync(
            string mailbox,
            CancellationToken cancellationToken) =>
            Task.FromResult<Domain.Connections.ConnectionLease?>(null);

        public Task ReleaseConnectionAsync(string nodeId, string connectionId, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
