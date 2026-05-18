using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace MediatRR;

/// <summary>
/// Internal implementation of the mediator pattern.
/// </summary>
internal sealed class Mediator(NotificationChannel notificationChannel, IServiceScopeFactory scopeFactory) : IMediator
{
    public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var requestType = request.GetType();

        var behaviors = sp.GetServices(HandlerCache.PipelineBehaviorType(requestType, typeof(TResponse)));

        Task<TResponse> InvokeHandler()
        {
            var handler = sp.GetService(HandlerCache.RequestHandlerType(requestType, typeof(TResponse)))
                ?? throw new InvalidOperationException($"No handler registered for {requestType}.");
            return HandlerCache.InvokeRequestHandler<TResponse>(handler, request, cancellationToken);
        }

        var pipeline = behaviors
            .Reverse()
            .Aggregate((Func<Task<TResponse>>)InvokeHandler,
                (next, behavior) => () => HandlerCache.InvokePipelineBehavior<TResponse>(behavior, request, next, cancellationToken));

        return await pipeline().ConfigureAwait(false);
    }

    public async Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        if (notification is null) throw new ArgumentNullException(nameof(notification));

        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var runtimeType = notification.GetType();

        var behaviors = sp.GetServices(HandlerCache.NotificationBehaviorType(runtimeType));

        Task InternalPublish()
        {
            // Only enqueue if at least one handler is registered. Behaviors still run regardless.
            var anyHandler = sp.GetService(HandlerCache.NotificationHandlerType(runtimeType)) != null;
            if (!anyHandler) return Task.CompletedTask;
            return notificationChannel.AddToChannel(new NotificationPublishContext(notification, runtimeType), cancellationToken).AsTask();
        }

        var pipeline = behaviors
            .Reverse()
            .Aggregate((Func<Task>)InternalPublish,
                (next, behavior) => () => HandlerCache.InvokeNotificationBehavior(behavior, notification, next, cancellationToken));

        await pipeline().ConfigureAwait(false);
    }

    public async IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamRequest<TResponse> request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var requestType = request.GetType();

        var behaviors = sp.GetServices(HandlerCache.StreamBehaviorType(requestType, typeof(TResponse)));

        IAsyncEnumerable<TResponse> InvokeHandler()
        {
            var handler = sp.GetService(HandlerCache.StreamRequestHandlerType(requestType, typeof(TResponse)))
                ?? throw new InvalidOperationException($"No stream handler registered for {requestType}.");
            return HandlerCache.InvokeStreamHandler<TResponse>(handler, request, cancellationToken);
        }

        var pipeline = behaviors
            .Reverse()
            .Aggregate((Func<IAsyncEnumerable<TResponse>>)InvokeHandler,
                (next, behavior) => () => HandlerCache.InvokeStreamBehavior<TResponse>(behavior, request, next, cancellationToken));

        await foreach (var item in pipeline().WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }
}