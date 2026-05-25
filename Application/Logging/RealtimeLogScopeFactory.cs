using Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Application.Logging;

public interface IRealtimeLogScopeFactory
{
    IDisposable BeginScope(ILogger logger);
}

public sealed class RealtimeLogScopeFactory(IOptions<RealtimeServiceOptions> options) : IRealtimeLogScopeFactory
{
    private readonly RealtimeLogScope _scope = new(options.Value.ServiceName, options.Value.NodeId);

    public IDisposable BeginScope(ILogger logger)
    {
        return logger.BeginScope(_scope.ToDictionary()) ?? NullScope.Instance;
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
