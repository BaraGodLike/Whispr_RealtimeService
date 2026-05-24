using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace Realtime.Services.Connections;

public sealed class WebSocketConnectionRegistry : IWebSocketConnectionRegistry
{
    private readonly ConcurrentDictionary<string, ConnectionContext> _connections = new(StringComparer.Ordinal);

    public ConnectionContext Add(WebSocket socket)
    {
        var connection = new ConnectionContext(Guid.NewGuid().ToString("N"), socket);
        _connections[connection.ConnectionId] = connection;
        return connection;
    }

    public bool TryGet(string connectionId, out ConnectionContext? connection)
    {
        return _connections.TryGetValue(connectionId, out connection);
    }

    public IReadOnlyCollection<ConnectionContext> Snapshot() => _connections.Values.ToArray();

    public bool TryMarkAuthenticated(string connectionId, string userId, IReadOnlyList<string> mailboxes)
    {
        if (!_connections.TryGetValue(connectionId, out var connection))
        {
            return false;
        }

        connection.MarkAuthenticated(userId, mailboxes);
        return true;
    }

    public async Task<bool> SendAsync(string connectionId, string payload, CancellationToken cancellationToken)
    {
        if (!_connections.TryGetValue(connectionId, out var connection))
        {
            return false;
        }

        return await connection.SendTextAsync(payload, cancellationToken);
    }

    public async Task CloseAsync(
        string connectionId,
        WebSocketCloseStatus closeStatus,
        string description,
        CancellationToken cancellationToken)
    {
        if (_connections.TryGetValue(connectionId, out var connection))
        {
            await connection.CloseAsync(closeStatus, description, cancellationToken);
        }
    }

    public bool Remove(string connectionId, out ConnectionContext? connection)
    {
        return _connections.TryRemove(connectionId, out connection);
    }
}
