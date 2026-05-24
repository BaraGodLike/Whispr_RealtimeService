using System.Net.WebSockets;

namespace Realtime.Services.Connections;

public interface IWebSocketConnectionRegistry
{
    ConnectionContext Add(WebSocket socket);

    bool TryGet(string connectionId, out ConnectionContext? connection);

    IReadOnlyCollection<ConnectionContext> Snapshot();

    bool TryMarkAuthenticated(string connectionId, string userId, IReadOnlyList<string> mailboxes);

    Task<bool> SendAsync(string connectionId, string payload, CancellationToken cancellationToken);

    Task CloseAsync(
        string connectionId,
        WebSocketCloseStatus closeStatus,
        string description,
        CancellationToken cancellationToken);

    bool Remove(string connectionId, out ConnectionContext? connection);
}
