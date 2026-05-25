namespace Contracts.Protocol;

public sealed record AuthenticateCommandMessage(
    string UserId,
    string Nonce,
    string Alg,
    string Signature);

public sealed record SendMessageCommandMessage(
    string MsgId,
    string DestMailbox,
    string Payload);

public sealed record AckCommandMessage(string MsgId);

public sealed record AckBatchCommandMessage(IReadOnlyList<string> MsgIds);

public sealed record ResumeCommandMessage(int Limit);

public sealed record AuthenticatedMessage(bool Success, int RegisteredMailboxCount);

public sealed record SendMessageAcceptedMessage(bool Accepted);

public sealed record AckAcceptedMessage(bool Success);

public sealed record AckBatchAcceptedMessage(int AckedCount);

public sealed record IncomingMessageMessage(string MsgId, string Payload);

public sealed record ResumeMessageItem(string MsgId, string Payload);

public sealed record ResumeMessagesMessage(IReadOnlyList<ResumeMessageItem> Messages, bool HasMore);

public sealed record ErrorMessage(string Code, string Message);
