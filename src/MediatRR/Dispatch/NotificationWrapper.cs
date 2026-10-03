using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MediatRR.Dispatch;

internal abstract class NotificationWrapper
{
    public abstract Task Publish(INotification notification, IServiceProvider serviceProvider, Func<Task> enqueue, CancellationToken cancellationToken);

    public abstract IReadOnlyList<Func<CancellationToken, Task>> BuildHandlerPipelines(IServiceProvider serviceProvider, INotification notification);
}

internal sealed class NotificationWrapper<TNotification> : NotificationWrapper
    where TNotification : INotification
{
    public override Task Publish(INotification notification, IServiceProvider serviceProvider, Func<Task> enqueue, CancellationToken cancellationToken)
    {
        var typedNotification = (TNotification)notification;
        var behaviors = serviceProvider.GetServices<INotificationBehavior<TNotification>>();

        var pipeline = behaviors
            .Reverse()
            .Aggregate(enqueue, (next, behavior) => () => behavior.Handle(typedNotification, next, cancellationToken));

        return pipeline();
    }

    public override IReadOnlyList<Func<CancellationToken, Task>> BuildHandlerPipelines(IServiceProvider serviceProvider, INotification notification)
    {
        var typedNotification = (TNotification)notification;
        var handlers = serviceProvider.GetServices<INotificationHandler<TNotification>>().ToList();
        if (handlers.Count == 0)
        {
            return Array.Empty<Func<CancellationToken, Task>>();
        }

        var behaviorsReversed = serviceProvider.GetServices<INotificationHandlerBehavior<TNotification>>().Reverse().ToList();
        var pipelines = new List<Func<CancellationToken, Task>>(handlers.Count);
        foreach (var handler in handlers)
        {
            var currentHandler = handler;
            pipelines.Add(cancellationToken =>
            {
                Func<Task> invokeHandler = () => currentHandler.Handle(typedNotification, cancellationToken);
                var pipeline = behaviorsReversed.Aggregate(invokeHandler,
                    (next, behavior) => () => behavior.Handle(typedNotification, next, cancellationToken));
                return pipeline();
            });
        }

        return pipelines;
    }
}
