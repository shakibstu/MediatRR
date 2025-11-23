using System;
using MediatRR.Contract.Messaging;

namespace MediatRR;

internal class NotificationPublishContext(INotification message, Type type)
{
    public int RetriedCount { get; set; }
    public INotification Message { get; set; } = message;
    public Type Type { get; set; } = type;
}