using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MediatRR.Dispatch;

internal sealed class HandlerPipeline(Type handlerType, Func<CancellationToken, Task> invoke)
{
    public Type HandlerType { get; } = handlerType;

    public Func<CancellationToken, Task> Invoke { get; } = invoke;
}

internal abstract class NotificationWrapper
{
    public abstract Task Publish(INotification notification, IServiceProvider serviceProvider, Func<Task> enqueue, CancellationToken cancellationToken);

    public abstract IReadOnlyList<HandlerPipeline> BuildHandlerPipelines(IServiceProvider serviceProvider, INotification notification);

    public abstract IReadOnlyList<HandlerPipeline> BuildDeadLetterPipelines(IServiceProvider serviceProvider, INotification notification, DeadLettersInfo deadLetter);
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

    public override IReadOnlyList<HandlerPipeline> BuildHandlerPipelines(IServiceProvider serviceProvider, INotification notification)
    {
        var typedNotification = (TNotification)notification;
        var handlers = serviceProvider.GetServices<INotificationHandler<TNotification>>().ToList();
        if (handlers.Count == 0)
        {
            return Array.Empty<HandlerPipeline>();
        }

        var behaviorsReversed = serviceProvider.GetServices<INotificationHandlerBehavior<TNotification>>().Reverse().ToList();
        var pipelines = new List<HandlerPipeline>(handlers.Count);
        foreach (var handler in handlers)
        {
            var currentHandler = handler;
            pipelines.Add(new HandlerPipeline(currentHandler.GetType(), cancellationToken =>
            {
                Func<Task> invokeHandler = () => currentHandler.Handle(typedNotification, cancellationToken);
                var pipeline = behaviorsReversed.Aggregate(invokeHandler,
                    (next, behavior) => () => behavior.Handle(typedNotification, next, cancellationToken));
                return pipeline();
            }));
        }

        return pipelines;
    }

    public override IReadOnlyList<HandlerPipeline> BuildDeadLetterPipelines(IServiceProvider serviceProvider, INotification notification, DeadLettersInfo deadLetter)
    {
        var handlers = serviceProvider.GetServices<IDeadLetterHandler<TNotification>>().ToList();
        if (handlers.Count == 0)
        {
            return Array.Empty<HandlerPipeline>();
        }

        var typedDeadLetter = new DeadLetter<TNotification>((TNotification)notification, deadLetter.Exception, deadLetter.AttemptCount, deadLetter.HandlerType, deadLetter.LastAttemptedAt);
        var pipelines = new List<HandlerPipeline>(handlers.Count);
        foreach (var handler in handlers)
        {
            var currentHandler = handler;
            pipelines.Add(new HandlerPipeline(currentHandler.GetType(), cancellationToken => currentHandler.Handle(typedDeadLetter, cancellationToken)));
        }

        return pipelines;
    }
}
