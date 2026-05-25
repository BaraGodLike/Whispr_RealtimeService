namespace Application.Logging;

public sealed record RealtimeLogScope(string Service, string Instance)
{
    public IReadOnlyDictionary<string, object> ToDictionary() =>
        new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["service"] = Service,
            ["instance"] = Instance
        };
}
