using MediatRR.Contract.Messaging;
using System;

namespace MediatRR;

/// <summary>
/// Internal context that wraps a notification for processing through the notification channel.
/// Tracks retry attempts and maintains notification metadata.
/// </summary>
internal sealed class NotificationPublishContext(INotification message, Type type)
{
    /// <summary>
    /// Gets or sets the number of times this notification has been retried.
    /// </summary>
    public int RetriedCount { get; private set; }
    
    public void IncreaseRetry() => RetriedCount++;

    /// <summary>
    /// Gets or sets the notification message.
    /// </summary>
    public INotification Message { get; } = message;
    
    /// <summary>
    /// Gets or sets the runtime type of the notification.
    /// Used for handler resolution.
    /// </summary>
    public Type Type { get; } = type;
}