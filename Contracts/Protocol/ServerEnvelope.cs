namespace Contracts.Protocol;

public sealed record ServerEnvelope<T>(string Type, T Data);
