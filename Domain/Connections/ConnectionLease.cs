namespace Domain.Connections;

public sealed record ConnectionLease(string NodeId, string ConnectionId)
{
    public string ToRedisValue() => $"{NodeId}:{ConnectionId}";

    public static bool TryParse(string? value, out ConnectionLease? lease)
    {
        lease = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var separatorIndex = value.LastIndexOf(':');
        if (separatorIndex <= 0 || separatorIndex == value.Length - 1)
        {
            return false;
        }

        lease = new ConnectionLease(
            value[..separatorIndex],
            value[(separatorIndex + 1)..]);

        return true;
    }
}
