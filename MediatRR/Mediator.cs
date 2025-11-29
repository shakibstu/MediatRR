using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MediatRR;

/// <summary>
/// Internal implementation of the mediator pattern.
/// Handles request/response and publish/subscribe messaging patterns.
/// </summary>
internal sealed class Mediator(NotificationChannel notificationChannel, IServiceScopeFactory scopeFactory) : IMediator
{
    /// <summary>
    /// Sends a request to its handler and returns the response.
    /// </summary>
    private async Task<TResponse> SendWithResponse<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        // Resolve the handler type for this request
        var handlerType = typeof(IRequestHandler<,>).MakeGenericType(request.GetType(), typeof(TResponse));

        // Create a scope for this request
        using var scope = scopeFactory.CreateScope();

        var handler = scope.ServiceProvider.GetService(handlerType);

        if (handler == null)
        {
            throw new ArgumentException($"No Handler Defined for {request.GetType()}");
        }
        
        // Invoke the Handle method on the handler using reflection
        const string handleName = nameof(IRequestHandler<IRequest<TResponse>, TResponse>.Handle);
        return await ((Task<TResponse>)handler.GetType().GetMethod(handleName)!.Invoke(
            handler, [request, cancellationToken]))!;
    }

    /// <summary>
    /// Publishes a notification to the notification channel if a handler exists.
    /// </summary>
    private ValueTask PublishToChannel<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification
    {
        // Create a scope for this request
        using var scope = scopeFactory.CreateScope();
        var handlerType = typeof(INotificationHandler<>).MakeGenericType(notification.GetType());
        // Only add to channel if there's at least one handler registered
        return scope.ServiceProvider.GetService(handlerType) != null
            ? notificationChannel.AddToChannel(notification, cancellationToken)
            : new ValueTask(Task.CompletedTask);
    }

    /// <inheritdoc />
    public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        return await Execute(request, () => SendWithResponse(request, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Executes the request through the pipeline behaviors before invoking the handler.
    /// </summary>
    private async Task<TResponse> Execute<TRequest, TResponse>(TRequest request, Func<Task<TResponse>> handler, CancellationToken cancellationToken = default)
    {
        // Get all registered pipeline behaviors for this request type
        var behaviorType = typeof(IPipelineBehavior<,>).MakeGenericType(request.GetType(), typeof(TResponse));

        // Create a scope for this request
        using var scope = scopeFactory.CreateScope();

        var behaviors = scope.ServiceProvider.GetServices(behaviorType);

        // Build the pipeline by wrapping each behavior around the next
        var response = behaviors.Reverse()
            .Aggregate(handler,
                (next, behavior) => () =>
                    (Task<TResponse>)behavior.GetType()
                        .GetMethod(nameof(IPipelineBehavior<TRequest, TResponse>.Handle))!
                        .Invoke(behavior, [request, next, cancellationToken])).Invoke();
        return await response;
    }
    
    /// <summary>
    /// Executes the notification through the notification behaviors before publishing to the channel.
    /// </summary>
    private async Task ExecuteNotification<TRequest>(TRequest request, Func<Task> handler, CancellationToken cancellationToken = default)
    {
        // Get all registered notification behaviors for this notification type
        var behaviorType = typeof(INotificationBehavior<>).MakeGenericType(request.GetType());
        using var scope = scopeFactory.CreateScope();
        var behaviors = scope.ServiceProvider.GetServices(behaviorType);

        // Build the pipeline by wrapping each behavior around the next
        var response = behaviors.Reverse()
            .Aggregate(handler,
                (next, behavior) => () =>
                    (Task)behavior.GetType()
                        .GetMethod(nameof(INotificationBehavior<INotification>.Handle))!
                        .Invoke(behavior, [request, next, cancellationToken])).Invoke();
        await response;
    }

    /// <inheritdoc />
    public async Task Publish<TNotification>(TNotification notification,
        CancellationToken cancellationToken = default) where TNotification : INotification
    {
        // Execute through notification behaviors, then publish to channel
        await ExecuteNotification(notification, InternalPublish, cancellationToken);
        return;

        Task InternalPublish()
        {
            return PublishToChannel(notification, cancellationToken).AsTask();
        }

    }
}