namespace Domain.Messages;

public sealed record MessageEnqueuedNotification(string MessageId, string DestinationMailbox);
