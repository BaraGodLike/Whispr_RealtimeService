namespace Infrastructure.Grpc.Options;

public sealed class RelayGrpcOptions
{
    public const string SectionName = "RelayGrpc";

    public string Address { get; set; } = string.Empty;
}
