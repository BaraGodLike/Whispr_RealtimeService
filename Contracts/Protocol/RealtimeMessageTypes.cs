namespace Contracts.Protocol;

public static class RealtimeMessageTypes
{
    public const string Authenticate = "authenticate";
    public const string SendMessage = "send_message";
    public const string Ack = "ack";
    public const string AckBatch = "ack_batch";
    public const string Resume = "resume";

    public const string Authenticated = "authenticated";
    public const string SendMessageAccepted = "send_message_accepted";
    public const string AckAccepted = "ack_accepted";
    public const string AckBatchAccepted = "ack_batch_accepted";
    public const string IncomingMessage = "incoming_message";
    public const string ResumeMessages = "resume_messages";
    public const string Error = "error";
}
