using Domain.Messages;

namespace Application.Abstractions;

public interface IMessageEnqueuedHandler
{
    Task HandleAsync(MessageEnqueuedNotification notification, CancellationToken cancellationToken);
}
