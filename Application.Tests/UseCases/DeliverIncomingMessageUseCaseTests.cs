using Application.Abstractions;
using Application.Exceptions;
using Application.Options;
using Application.UseCases;
using Domain.Connections;
using Domain.Messages;
using Microsoft.Extensions.Options;

namespace Application.Tests.UseCases;

[TestClass]
public sealed class DeliverIncomingMessageUseCaseTests
{
    [TestMethod]
    public async Task ExecuteAsync_SkipsWhenMailboxIsOffline()
    {
        var useCase = new DeliverIncomingMessageUseCase(
            new FakeConnectionLeaseStore(null),
            new FakeRelayClient(),
            Microsoft.Extensions.Options.Options.Create(new RealtimeServiceOptions()));

        var result = await useCase.ExecuteAsync(
            new MessageEnqueuedNotification("msg-1", "mbx-1"),
            CancellationToken.None);

        Assert.IsFalse(result.ShouldDeliver);
        Assert.AreEqual(DeliverySkipReason.Offline, result.SkipReason);
    }

    [TestMethod]
    public async Task ExecuteAsync_DeliversMessageForCurrentNode()
    {
        var useCase = new DeliverIncomingMessageUseCase(
            new FakeConnectionLeaseStore(new ConnectionLease("realtime-1", "conn-1")),
            new FakeRelayClient([9, 8, 7]),
            Microsoft.Extensions.Options.Options.Create(new RealtimeServiceOptions { NodeId = "realtime-1" }));

        var result = await useCase.ExecuteAsync(
            new MessageEnqueuedNotification("msg-1", "mbx-1"),
            CancellationToken.None);

        Assert.IsTrue(result.ShouldDeliver);
        Assert.AreEqual("conn-1", result.ConnectionId);
        CollectionAssert.AreEqual(new byte[] { 9, 8, 7 }, result.Payload!);
    }

    [TestMethod]
    public async Task ExecuteAsync_SkipsWhenRelayMessageIsMissing()
    {
        var useCase = new DeliverIncomingMessageUseCase(
            new FakeConnectionLeaseStore(new ConnectionLease("realtime-1", "conn-1")),
            new FakeRelayClient(throwNotFound: true),
            Microsoft.Extensions.Options.Options.Create(new RealtimeServiceOptions { NodeId = "realtime-1" }));

        var result = await useCase.ExecuteAsync(
            new MessageEnqueuedNotification("msg-1", "mbx-1"),
            CancellationToken.None);

        Assert.IsFalse(result.ShouldDeliver);
        Assert.AreEqual(DeliverySkipReason.MessageNotFound, result.SkipReason);
    }

    private sealed class FakeConnectionLeaseStore(ConnectionLease? lease) : IConnectionLeaseStore
    {
        public Task<RegisterConnectionLeaseResult> RegisterAuthenticatedConnectionAsync(
            string nodeId,
            string connectionId,
            IReadOnlyCollection<string> mailboxes,
            TimeSpan ttl,
            CancellationToken cancellationToken) =>
            Task.FromResult(new RegisterConnectionLeaseResult(mailboxes.Count, Array.Empty<string>()));

        public Task<RefreshConnectionLeaseResult> RefreshConnectionAsync(
            string nodeId,
            string connectionId,
            TimeSpan ttl,
            CancellationToken cancellationToken) =>
            Task.FromResult(new RefreshConnectionLeaseResult(false, Array.Empty<string>()));

        public Task<ConnectionLease?> GetLeaseByMailboxAsync(string mailbox, CancellationToken cancellationToken) =>
            Task.FromResult(lease);

        public Task ReleaseConnectionAsync(string nodeId, string connectionId, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FakeRelayClient(byte[]? payload = null, bool throwNotFound = false) : IRelayClient
    {
        public Task<bool> EnqueueMessageAsync(
            string messageId,
            string destinationMailbox,
            byte[] payload,
            CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<byte[]> GetMessageAsync(string messageId, CancellationToken cancellationToken)
        {
            if (throwNotFound)
            {
                throw new RelayMessageNotFoundException("Not found.");
            }

            return Task.FromResult(payload ?? Array.Empty<byte>());
        }

        public Task<GetPendingMessagesResult> GetPendingMessagesAsync(
            IReadOnlyCollection<string> mailboxes,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult(new GetPendingMessagesResult(Array.Empty<PendingMessage>(), false));

        public Task<bool> AckMessageAsync(string messageId, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<int> AckMessagesBatchAsync(
            IReadOnlyCollection<string> messageIds,
            CancellationToken cancellationToken) =>
            Task.FromResult(messageIds.Count);
    }
}
