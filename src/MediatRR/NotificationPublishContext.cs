using MediatRR.Contract.Messaging;
using System;

namespace MediatRR;

/// <summary>
/// Internal context that wraps a notification for processing through the notification channel.
/// </summary>
internal sealed class NotificationPublishContext(INotification message, Type type)
{
    /// <summary>
    /// Gets the notification message.
    /// </summary>
    public INotification Message { get; } = message;

    /// <summary>
    /// Gets the runtime type of the notification.
    /// Used for handler resolution.
    /// </summary>
    public Type Type { get; } = type;
}
