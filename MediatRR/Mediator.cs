using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace MediatRR;

internal class Mediator(IServiceProvider serviceProvider) : IMediator
{
    private async Task<TResponse> SendWithResponse<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        var handlerType = typeof(IRequestHandler<,>).MakeGenericType(request.GetType(), typeof(TResponse));

        var handler = serviceProvider.GetService(handlerType);

        if (handler == null)
        {
            throw new ArgumentException($"No Handler Defined for {request.GetType()}");
        }
        const string handleName = nameof(IRequestHandler<IRequest<object>,object>.Handle);
        return await ((Task<TResponse>)handler.GetType().GetMethod(handleName)!.Invoke(
            handler, [request, cancellationToken]))!;
    }

    private ValueTask PublishToChannel<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification
    {
        return serviceProvider.GetRequiredService<NotificationChannel>().AddToChannel(notification, cancellationToken);
    }

    public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        return await Execute(request, () => SendWithResponse(request, cancellationToken), cancellationToken);
    }

    private async Task<TResponse> Execute<TRequest, TResponse>(TRequest request, Func<Task<TResponse>> handler, CancellationToken cancellationToken = default)
    {
        var behaviorType = typeof(IPipelineBehavior<,>).MakeGenericType(request.GetType(), typeof(TResponse));
        var behaviors = serviceProvider.GetServices(behaviorType);

        var response = behaviors.Reverse()
            .Aggregate(handler,
                (next, behavior) => () =>
                    (Task<TResponse>)behavior.GetType()
                        .GetMethod(nameof(IPipelineBehavior<IRequest<TResponse>, TResponse>.Handle))!
                        .Invoke(behavior, [request, next, cancellationToken])).Invoke();
        return await response;
    }
    private async Task ExecuteNotification<TRequest>(TRequest request, Func<Task> handler, CancellationToken cancellationToken = default)
    {
        var behaviorType = typeof(INotificationBehavior<>).MakeGenericType(request.GetType());
        var behaviors = serviceProvider.GetServices(behaviorType);

        var response = behaviors.Reverse()
            .Aggregate(handler,
                (next, behavior) => () =>
                    (Task)behavior.GetType()
                        .GetMethod(nameof(INotificationBehavior<INotification>.Handle))!
                        .Invoke(behavior, [request, next, cancellationToken])).Invoke();
        await response;
    }

    public async Task Publish<TNotification>(TNotification notification,
        CancellationToken cancellationToken = default) where TNotification : INotification
    {
        await ExecuteNotification(notification, InternalPublish, cancellationToken);
        return;

        Task InternalPublish()
        {
            return PublishToChannel(notification, cancellationToken).AsTask();
        }

    }
}