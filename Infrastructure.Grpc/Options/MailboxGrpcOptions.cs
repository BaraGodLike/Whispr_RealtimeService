namespace Infrastructure.Grpc.Options;

public sealed class MailboxGrpcOptions
{
    public const string SectionName = "MailboxGrpc";

    public string Address { get; set; } = string.Empty;
}
