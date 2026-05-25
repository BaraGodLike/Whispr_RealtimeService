namespace Domain.Messages;

public sealed record PendingMessage(string MessageId, byte[] Payload);
