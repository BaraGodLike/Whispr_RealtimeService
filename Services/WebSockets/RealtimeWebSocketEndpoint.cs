using System.Net.WebSockets;
using System.Text;
using Application.Exceptions;
using Application.Logging;
using Application.Options;
using Application.UseCases;
using Contracts.Protocol;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Realtime.Services.Connections;

namespace Realtime.Services.WebSockets;

public sealed class RealtimeWebSocketEndpoint(
    IWebSocketConnectionRegistry connectionRegistry,
    AuthenticateConnectionUseCase authenticateConnectionUseCase,
    SendRealtimeMessageUseCase sendRealtimeMessageUseCase,
    AcknowledgeMessageUseCase acknowledgeMessageUseCase,
    AcknowledgeMessagesBatchUseCase acknowledgeMessagesBatchUseCase,
    ResumePendingMessagesUseCase resumePendingMessagesUseCase,
    DisconnectConnectionUseCase disconnectConnectionUseCase,
    IOptions<RealtimeServiceOptions> realtimeOptions,
    IRealtimeLogScopeFactory logScopeFactory,
    ILogger<RealtimeWebSocketEndpoint> logger)
{
    private const int ReceiveBufferSize = 8 * 1024;

    public async Task HandleAsync(HttpContext httpContext)
    {
        using var logScope = logScopeFactory.BeginScope(logger);
        if (!httpContext.WebSockets.IsWebSocketRequest)
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await httpContext.WebSockets.AcceptWebSocketAsync();
        var connection = connectionRegistry.Add(socket);

        try
        {
            await ProcessConnectionAsync(connection, httpContext.RequestAborted);
        }
        catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
        {
        }
        finally
        {
            try
            {
                await disconnectConnectionUseCase.ExecuteAsync(connection.ConnectionId, CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "Connection cleanup failed. ExceptionType={ExceptionType}",
                    exception.GetType().Name);
            }

            connectionRegistry.Remove(connection.ConnectionId, out _);
        }
    }

    private async Task ProcessConnectionAsync(ConnectionContext connection, CancellationToken cancellationToken)
    {
        var buffer = new byte[ReceiveBufferSize];

        while (!cancellationToken.IsCancellationRequested && connection.Socket.State == WebSocketState.Open)
        {
            var message = await ReceiveMessageAsync(connection.Socket, buffer, realtimeOptions.Value.MaxPayloadBytes, cancellationToken);
            if (message is null)
            {
                return;
            }

            connection.Touch();

            if (!await DispatchMessageAsync(connection, message, cancellationToken))
            {
                return;
            }
        }
    }

    private async Task<bool> DispatchMessageAsync(
        ConnectionContext connection,
        string rawMessage,
        CancellationToken cancellationToken)
    {
        ClientEnvelope? envelope;
        try
        {
            envelope = RealtimeJson.DeserializeClientEnvelope(rawMessage);
        }
        catch (Exception)
        {
            await SendErrorAsync(connection.ConnectionId, RealtimeErrorCodes.InvalidRequest, "Invalid JSON payload.", cancellationToken);
            return true;
        }

        if (envelope is null || string.IsNullOrWhiteSpace(envelope.Type))
        {
            await SendErrorAsync(connection.ConnectionId, RealtimeErrorCodes.InvalidRequest, "Message envelope is invalid.", cancellationToken);
            return true;
        }

        try
        {
            return envelope.Type switch
            {
                RealtimeMessageTypes.Authenticate => await HandleAuthenticateAsync(connection, envelope, cancellationToken),
                RealtimeMessageTypes.SendMessage => await RequireAuthenticatedAsync(connection, () => HandleSendMessageAsync(connection, envelope, cancellationToken), cancellationToken),
                RealtimeMessageTypes.Ack => await RequireAuthenticatedAsync(connection, () => HandleAckAsync(connection, envelope, cancellationToken), cancellationToken),
                RealtimeMessageTypes.AckBatch => await RequireAuthenticatedAsync(connection, () => HandleAckBatchAsync(connection, envelope, cancellationToken), cancellationToken),
                RealtimeMessageTypes.Resume => await RequireAuthenticatedAsync(connection, () => HandleResumeAsync(connection, envelope, cancellationToken), cancellationToken),
                _ => await HandleUnknownMessageAsync(connection, cancellationToken)
            };
        }
        catch (ClientRequestValidationException)
        {
            await SendErrorAsync(connection.ConnectionId, RealtimeErrorCodes.InvalidRequest, "Request validation failed.", cancellationToken);
            return true;
        }
        catch (AuthenticationFailedException)
        {
            await SendErrorAsync(connection.ConnectionId, RealtimeErrorCodes.Forbidden, "Authentication failed.", cancellationToken);
            await connectionRegistry.CloseAsync(connection.ConnectionId, WebSocketCloseStatus.PolicyViolation, "Authentication failed.", cancellationToken);
            return false;
        }
        catch (Grpc.Core.RpcException rpcException) when (rpcException.StatusCode is
                   Grpc.Core.StatusCode.InvalidArgument or
                   Grpc.Core.StatusCode.AlreadyExists)
        {
            logger.LogWarning(
                "Realtime websocket upstream validation failed. GrpcStatusCode={GrpcStatusCode}",
                rpcException.StatusCode);

            await SendErrorAsync(connection.ConnectionId, RealtimeErrorCodes.InvalidRequest, "Request validation failed.", cancellationToken);
            return true;
        }
        catch (Grpc.Core.RpcException rpcException)
        {
            logger.LogWarning(
                "Realtime websocket upstream call failed. GrpcStatusCode={GrpcStatusCode}",
                rpcException.StatusCode);

            await SendErrorAsync(connection.ConnectionId, RealtimeErrorCodes.UpstreamUnavailable, "Upstream service is unavailable.", cancellationToken);
            return true;
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Realtime websocket command failed. ExceptionType={ExceptionType}",
                exception.GetType().Name);

            await SendErrorAsync(connection.ConnectionId, RealtimeErrorCodes.InternalError, "Internal server error.", cancellationToken);
            return true;
        }
    }

    private async Task<bool> HandleAuthenticateAsync(
        ConnectionContext connection,
        ClientEnvelope envelope,
        CancellationToken cancellationToken)
    {
        if (connection.IsAuthenticated)
        {
            await SendErrorAsync(connection.ConnectionId, RealtimeErrorCodes.InvalidRequest, "Connection is already authenticated.", cancellationToken);
            return true;
        }

        var message = RealtimeJson.DeserializeData<AuthenticateCommandMessage>(envelope.Data);
        if (message is null)
        {
            throw new ClientRequestValidationException("Authentication message is invalid.");
        }

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(message.Signature);
        }
        catch (FormatException)
        {
            throw new ClientRequestValidationException("Signature format is invalid.");
        }

        var result = await authenticateConnectionUseCase.ExecuteAsync(
            new AuthenticateConnectionCommand(
                connection.ConnectionId,
                message.UserId,
                message.Nonce,
                message.Alg,
                signature),
            cancellationToken);

        connectionRegistry.TryMarkAuthenticated(connection.ConnectionId, message.UserId, result.Mailboxes);

        foreach (var displacedConnectionId in result.DisplacedLocalConnectionIds)
        {
            if (!string.Equals(displacedConnectionId, connection.ConnectionId, StringComparison.Ordinal))
            {
                await connectionRegistry.CloseAsync(
                    displacedConnectionId,
                    WebSocketCloseStatus.PolicyViolation,
                    "Connection superseded.",
                    cancellationToken);
            }
        }

        await SendAsync(
            connection.ConnectionId,
            new ServerEnvelope<AuthenticatedMessage>(
                RealtimeMessageTypes.Authenticated,
                new AuthenticatedMessage(true, result.RegisteredMailboxCount)),
            cancellationToken);

        return true;
    }

    private async Task<bool> HandleSendMessageAsync(
        ConnectionContext connection,
        ClientEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var message = RealtimeJson.DeserializeData<SendMessageCommandMessage>(envelope.Data);
        if (message is null)
        {
            throw new ClientRequestValidationException("Send message command is invalid.");
        }

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(message.Payload);
        }
        catch (FormatException)
        {
            throw new ClientRequestValidationException("Payload format is invalid.");
        }

        if (payload.Length > realtimeOptions.Value.MaxPayloadBytes)
        {
            throw new ClientRequestValidationException("Payload size exceeds the configured maximum.");
        }

        var accepted = await sendRealtimeMessageUseCase.ExecuteAsync(
            new SendRealtimeMessageCommand(message.MsgId, message.DestMailbox, payload),
            cancellationToken);

        await SendAsync(
            connection.ConnectionId,
            new ServerEnvelope<SendMessageAcceptedMessage>(
                RealtimeMessageTypes.SendMessageAccepted,
                new SendMessageAcceptedMessage(accepted)),
            cancellationToken);

        return true;
    }

    private async Task<bool> HandleAckAsync(
        ConnectionContext connection,
        ClientEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var message = RealtimeJson.DeserializeData<AckCommandMessage>(envelope.Data);
        if (message is null)
        {
            throw new ClientRequestValidationException("Ack command is invalid.");
        }

        var success = await acknowledgeMessageUseCase.ExecuteAsync(message.MsgId, cancellationToken);

        await SendAsync(
            connection.ConnectionId,
            new ServerEnvelope<AckAcceptedMessage>(
                RealtimeMessageTypes.AckAccepted,
                new AckAcceptedMessage(success)),
            cancellationToken);

        return true;
    }

    private async Task<bool> HandleAckBatchAsync(
        ConnectionContext connection,
        ClientEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var message = RealtimeJson.DeserializeData<AckBatchCommandMessage>(envelope.Data);
        if (message is null)
        {
            throw new ClientRequestValidationException("Ack batch command is invalid.");
        }

        var ackedCount = await acknowledgeMessagesBatchUseCase.ExecuteAsync(message.MsgIds, cancellationToken);

        await SendAsync(
            connection.ConnectionId,
            new ServerEnvelope<AckBatchAcceptedMessage>(
                RealtimeMessageTypes.AckBatchAccepted,
                new AckBatchAcceptedMessage(ackedCount)),
            cancellationToken);

        return true;
    }

    private async Task<bool> HandleResumeAsync(
        ConnectionContext connection,
        ClientEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var message = RealtimeJson.DeserializeData<ResumeCommandMessage>(envelope.Data);
        if (message is null)
        {
            throw new ClientRequestValidationException("Resume command is invalid.");
        }

        var result = await resumePendingMessagesUseCase.ExecuteAsync(
            connection.Mailboxes,
            message.Limit,
            cancellationToken);

        var responseMessages = result.Messages
            .Select(pendingMessage => new ResumeMessageItem(
                pendingMessage.MessageId,
                Convert.ToBase64String(pendingMessage.Payload)))
            .ToArray();

        await SendAsync(
            connection.ConnectionId,
            new ServerEnvelope<ResumeMessagesMessage>(
                RealtimeMessageTypes.ResumeMessages,
                new ResumeMessagesMessage(responseMessages, result.HasMore)),
            cancellationToken);

        return true;
    }

    private async Task<bool> HandleUnknownMessageAsync(ConnectionContext connection, CancellationToken cancellationToken)
    {
        await SendErrorAsync(connection.ConnectionId, RealtimeErrorCodes.InvalidRequest, "Unknown message type.", cancellationToken);
        return true;
    }

    private async Task<bool> RequireAuthenticatedAsync(
        ConnectionContext connection,
        Func<Task<bool>> action,
        CancellationToken cancellationToken)
    {
        if (connection.IsAuthenticated)
        {
            return await action();
        }

        await SendErrorAsync(connection.ConnectionId, RealtimeErrorCodes.Unauthenticated, "Authenticate first.", cancellationToken);
        await connectionRegistry.CloseAsync(connection.ConnectionId, WebSocketCloseStatus.PolicyViolation, "Authenticate first.", cancellationToken);
        return false;
    }

    private async Task SendErrorAsync(
        string connectionId,
        string code,
        string message,
        CancellationToken cancellationToken)
    {
        await SendAsync(
            connectionId,
            new ServerEnvelope<ErrorMessage>(
                RealtimeMessageTypes.Error,
                new ErrorMessage(code, message)),
            cancellationToken);
    }

    private async Task SendAsync<T>(
        string connectionId,
        ServerEnvelope<T> envelope,
        CancellationToken cancellationToken)
    {
        await connectionRegistry.SendAsync(connectionId, RealtimeJson.Serialize(envelope), cancellationToken);
    }

    private static async Task<string?> ReceiveMessageAsync(
        WebSocket socket,
        byte[] buffer,
        int maxPayloadBytes,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                throw new ClientRequestValidationException("Only text websocket messages are supported.");
            }

            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                break;
            }
        }

        if (stream.Length > maxPayloadBytes * 2L)
        {
            throw new ClientRequestValidationException("WebSocket message is too large.");
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
