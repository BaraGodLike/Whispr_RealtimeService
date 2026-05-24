using System.Text.Json;

namespace Contracts.Protocol;

public sealed record ClientEnvelope(string Type, JsonElement Data);
