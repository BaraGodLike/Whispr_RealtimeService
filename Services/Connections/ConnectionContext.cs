using System.Net.WebSockets;
using System.Text;

namespace Realtime.Services.Connections;

public sealed class ConnectionContext
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly object _sync = new();

    public ConnectionContext(string connectionId, WebSocket socket)
    {
        ConnectionId = connectionId;
        Socket = socket;
        ConnectedAt = DateTimeOffset.UtcNow;
        LastSeenAt = ConnectedAt;
        LastLeaseRefreshAt = ConnectedAt;
    }

    public string ConnectionId { get; }

    public WebSocket Socket { get; }

    public DateTimeOffset ConnectedAt { get; }

    public DateTimeOffset LastSeenAt { get; private set; }

    public DateTimeOffset LastLeaseRefreshAt { get; private set; }

    public bool IsAuthenticated { get; private set; }

    public string? UserId { get; private set; }

    public IReadOnlyList<string> Mailboxes { get; private set; } = Array.Empty<string>();

    public void Touch()
    {
        lock (_sync)
        {
            LastSeenAt = DateTimeOffset.UtcNow;
        }
    }

    public void MarkAuthenticated(string userId, IReadOnlyList<string> mailboxes)
    {
        lock (_sync)
        {
            UserId = userId;
            Mailboxes = mailboxes;
            IsAuthenticated = true;
            LastLeaseRefreshAt = DateTimeOffset.UtcNow;
            LastSeenAt = DateTimeOffset.UtcNow;
        }
    }

    public void MarkLeaseRefreshed()
    {
        lock (_sync)
        {
            LastLeaseRefreshAt = DateTimeOffset.UtcNow;
        }
    }

    public async Task<bool> SendTextAsync(string payload, CancellationToken cancellationToken)
    {
        if (Socket.State != WebSocketState.Open)
        {
            return false;
        }

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            if (Socket.State != WebSocketState.Open)
            {
                return false;
            }

            var buffer = Encoding.UTF8.GetBytes(payload);
            await Socket.SendAsync(buffer, WebSocketMessageType.Text, true, cancellationToken);
            return true;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task CloseAsync(
        WebSocketCloseStatus closeStatus,
        string description,
        CancellationToken cancellationToken)
    {
        if (Socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
        {
            return;
        }

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            if (Socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await Socket.CloseAsync(closeStatus, description, cancellationToken);
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }
}
