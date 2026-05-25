using System.Text.Json;
using Application.Abstractions;
using Domain.Connections;
using StackExchange.Redis;

namespace Infrastructure.Redis;

internal sealed class RedisConnectionLeaseStore(IConnectionMultiplexer connectionMultiplexer) : IConnectionLeaseStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<RegisterConnectionLeaseResult> RegisterAuthenticatedConnectionAsync(
        string nodeId,
        string connectionId,
        IReadOnlyCollection<string> mailboxes,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var database = connectionMultiplexer.GetDatabase();
        var normalizedMailboxes = mailboxes
            .Where(mailbox => !string.IsNullOrWhiteSpace(mailbox))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var displacedConnectionIds = new HashSet<string>(StringComparer.Ordinal);
        var mailboxKeys = normalizedMailboxes
            .Select(GetMailboxKey)
            .ToArray();

        if (mailboxKeys.Length > 0)
        {
            var existingLeases = await database.StringGetAsync(mailboxKeys);

            foreach (var existingLease in existingLeases)
            {
                if (!ConnectionLease.TryParse(existingLease, out var parsedLease) ||
                    parsedLease is null ||
                    !string.Equals(parsedLease.NodeId, nodeId, StringComparison.Ordinal) ||
                    string.Equals(parsedLease.ConnectionId, connectionId, StringComparison.Ordinal))
                {
                    continue;
                }

                displacedConnectionIds.Add(parsedLease.ConnectionId);
            }
        }

        var connectionIndex = new ConnectionIndexDocument(nodeId, normalizedMailboxes);
        var tasks = new List<Task>(normalizedMailboxes.Length + 1)
        {
            database.StringSetAsync(
                GetConnectionIndexKey(connectionId),
                JsonSerializer.Serialize(connectionIndex, JsonOptions),
                ttl)
        };

        var leaseValue = new ConnectionLease(nodeId, connectionId).ToRedisValue();
        tasks.AddRange(normalizedMailboxes.Select(mailbox =>
            database.StringSetAsync(GetMailboxKey(mailbox), leaseValue, ttl)));

        await Task.WhenAll(tasks);

        return new RegisterConnectionLeaseResult(normalizedMailboxes.Length, displacedConnectionIds.ToArray());
    }

    public async Task<RefreshConnectionLeaseResult> RefreshConnectionAsync(
        string nodeId,
        string connectionId,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var database = connectionMultiplexer.GetDatabase();
        var document = await GetConnectionIndexDocumentAsync(database, connectionId);
        if (document is null || !string.Equals(document.NodeId, nodeId, StringComparison.Ordinal))
        {
            return new RefreshConnectionLeaseResult(true, Array.Empty<string>());
        }

        var expectedLeaseValue = new ConnectionLease(nodeId, connectionId).ToRedisValue();
        foreach (var mailbox in document.Mailboxes)
        {
            var currentLease = await database.StringGetAsync(GetMailboxKey(mailbox));
            if (!string.Equals(currentLease, expectedLeaseValue, StringComparison.Ordinal))
            {
                return new RefreshConnectionLeaseResult(true, document.Mailboxes);
            }
        }

        var tasks = new List<Task>(document.Mailboxes.Count + 1)
        {
            database.KeyExpireAsync(GetConnectionIndexKey(connectionId), ttl)
        };

        tasks.AddRange(document.Mailboxes.Select(mailbox => database.KeyExpireAsync(GetMailboxKey(mailbox), ttl)));
        await Task.WhenAll(tasks);

        return new RefreshConnectionLeaseResult(false, document.Mailboxes);
    }

    public async Task<ConnectionLease?> GetLeaseByMailboxAsync(string mailbox, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var database = connectionMultiplexer.GetDatabase();
        var value = await database.StringGetAsync(GetMailboxKey(mailbox));

        return ConnectionLease.TryParse(value, out var lease) ? lease : null;
    }

    public async Task ReleaseConnectionAsync(string nodeId, string connectionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var database = connectionMultiplexer.GetDatabase();
        var document = await GetConnectionIndexDocumentAsync(database, connectionId);
        if (document is null || !string.Equals(document.NodeId, nodeId, StringComparison.Ordinal))
        {
            return;
        }

        var expectedLeaseValue = new ConnectionLease(nodeId, connectionId).ToRedisValue();
        foreach (var mailbox in document.Mailboxes)
        {
            var mailboxKey = GetMailboxKey(mailbox);
            var currentLease = await database.StringGetAsync(mailboxKey);
            if (string.Equals(currentLease, expectedLeaseValue, StringComparison.Ordinal))
            {
                await database.KeyDeleteAsync(mailboxKey);
            }
        }

        await database.KeyDeleteAsync(GetConnectionIndexKey(connectionId));
    }

    private static RedisKey GetMailboxKey(string mailbox) => $"conn:{mailbox}";

    private static RedisKey GetConnectionIndexKey(string connectionId) => $"connidx:{connectionId}";

    private static async Task<ConnectionIndexDocument?> GetConnectionIndexDocumentAsync(
        IDatabase database,
        string connectionId)
    {
        var value = await database.StringGetAsync(GetConnectionIndexKey(connectionId));
        if (value.IsNullOrEmpty)
        {
            return null;
        }

        return JsonSerializer.Deserialize<ConnectionIndexDocument>((string)value!, JsonOptions);
    }

    private sealed record ConnectionIndexDocument(string NodeId, IReadOnlyList<string> Mailboxes);
}
