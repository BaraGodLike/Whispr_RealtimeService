using Application.Abstractions;
using Application.Exceptions;
using Application.Options;
using Application.UseCases;
using Domain.Messages;
using Microsoft.Extensions.Options;

namespace Application.Tests.UseCases;

[TestClass]
public sealed class ResumePendingMessagesUseCaseTests
{
    [TestMethod]
    public async Task ExecuteAsync_ThrowsWhenLimitIsOutOfRange()
    {
        var useCase = new ResumePendingMessagesUseCase(
            new FakeRelayClient(),
            Microsoft.Extensions.Options.Options.Create(new RealtimeServiceOptions { MaxResumeBatchSize = 500 }));

        await Assert.ThrowsExactlyAsync<ClientRequestValidationException>(() =>
            useCase.ExecuteAsync(["mbx-1"], 501, CancellationToken.None));
    }

    [TestMethod]
    public async Task ExecuteAsync_DelegatesToRelayForValidRequest()
    {
        var relayClient = new FakeRelayClient();
        var useCase = new ResumePendingMessagesUseCase(
            relayClient,
            Microsoft.Extensions.Options.Options.Create(new RealtimeServiceOptions { MaxResumeBatchSize = 500 }));

        var result = await useCase.ExecuteAsync(["mbx-1"], 100, CancellationToken.None);

        Assert.AreEqual(100, relayClient.LastLimit);
        Assert.IsTrue(result.HasMore);
        Assert.HasCount(1, result.Messages);
    }

    private sealed class FakeRelayClient : IRelayClient
    {
        public int LastLimit { get; private set; }

        public Task<bool> EnqueueMessageAsync(
            string messageId,
            string destinationMailbox,
            byte[] payload,
            CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<byte[]> GetMessageAsync(string messageId, CancellationToken cancellationToken) =>
            Task.FromResult(Array.Empty<byte>());

        public Task<GetPendingMessagesResult> GetPendingMessagesAsync(
            IReadOnlyCollection<string> mailboxes,
            int limit,
            CancellationToken cancellationToken)
        {
            LastLimit = limit;
            return Task.FromResult(new GetPendingMessagesResult(
                [new PendingMessage("msg-1", [1, 2, 3])],
                true));
        }

        public Task<bool> AckMessageAsync(string messageId, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<int> AckMessagesBatchAsync(
            IReadOnlyCollection<string> messageIds,
            CancellationToken cancellationToken) =>
            Task.FromResult(messageIds.Count);
    }
}
