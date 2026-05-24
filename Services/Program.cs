using Application;
using Application.Abstractions;
using Application.Logging;
using Application.Options;
using Infrastructure.Grpc;
using Infrastructure.Messaging;
using Infrastructure.Redis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Realtime.Services.Connections;
using Realtime.Services.HostedServices;
using Realtime.Services.Messaging;
using Realtime.Services.WebSockets;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);

builder.Services
    .AddOptions<RealtimeServiceOptions>()
    .Bind(builder.Configuration.GetSection(RealtimeServiceOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.ServiceName), "Realtime service name is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.NodeId), "Realtime node id is required.")
    .Validate(options => options.RedisLeaseTtl > TimeSpan.Zero, "Redis lease TTL must be positive.")
    .Validate(options => options.LeaseRefreshInterval > TimeSpan.Zero, "Lease refresh interval must be positive.")
    .Validate(options => options.HeartbeatCheckInterval > TimeSpan.Zero, "Heartbeat check interval must be positive.")
    .Validate(options => options.AuthTimeout > TimeSpan.Zero, "Authentication timeout must be positive.")
    .Validate(options => options.MaxPayloadBytes > 0, "Maximum payload size must be positive.")
    .Validate(options => options.MaxResumeBatchSize is >= 1 and <= 500, "Maximum resume batch size must be between 1 and 500.")
    .Validate(options => options.RelayGetMessageRetry.MaxAttempts >= 1, "Relay retry attempts must be at least 1.")
    .ValidateOnStart();

builder.Services.AddApplication();
builder.Services.AddRedisInfrastructure(builder.Configuration);
builder.Services.AddGrpcInfrastructure(builder.Configuration);
builder.Services.AddMessagingInfrastructure(builder.Configuration);

builder.Services.AddSingleton<IWebSocketConnectionRegistry, WebSocketConnectionRegistry>();
builder.Services.AddSingleton<IRealtimeLogScopeFactory, RealtimeLogScopeFactory>();
builder.Services.AddScoped<IMessageEnqueuedHandler, MessageEnqueuedHandler>();
builder.Services.AddHostedService<ConnectionMaintenanceService>();
builder.Services.AddScoped<RealtimeWebSocketEndpoint>();

var app = builder.Build();

var realtimeOptions = app.Services.GetRequiredService<IOptions<RealtimeServiceOptions>>().Value;

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = realtimeOptions.LeaseRefreshInterval
});

app.MapGet("/", () => Results.Ok(new { status = "ok" }));
app.Map("/ws", static async (HttpContext context, RealtimeWebSocketEndpoint endpoint) =>
{
    await endpoint.HandleAsync(context);
});

app.Run();
