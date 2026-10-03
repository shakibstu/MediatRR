using MediatRR.Contract.Messaging;
using MediatRR.Dispatch;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
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

        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            return await WrapperCache.ForRequest<TResponse>(request.GetType())
                .Handle(request, scope.ServiceProvider, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        if (notification is null) throw new ArgumentNullException(nameof(notification));

        var runtimeType = notification.GetType();
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var serviceProvider = scope.ServiceProvider;

            Task Enqueue()
            {
                if (!HasHandlers(serviceProvider, runtimeType)) return Task.CompletedTask;
                return EnqueueAsync(new NotificationPublishContext(notification, runtimeType), cancellationToken);
            }

            await WrapperCache.ForNotification(runtimeType)
                .Publish(notification, serviceProvider, Enqueue, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamRequest<TResponse> request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var stream = WrapperCache.ForStream<TResponse>(request.GetType())
                .Handle(request, scope.ServiceProvider, cancellationToken);

            await foreach (var item in stream.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                yield return item;
            }
        }
    }

    private static bool HasHandlers(IServiceProvider serviceProvider, Type notificationType)
    {
        var query = serviceProvider.GetService<IServiceProviderIsService>();
        return query is null || query.IsService(typeof(INotificationHandler<>).MakeGenericType(notificationType));
    }

    private async Task EnqueueAsync(NotificationPublishContext context, CancellationToken cancellationToken)
    {
        try
        {
            await notificationChannel.AddToChannel(context, cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            throw new InvalidOperationException("MediatRR notification processing has stopped; notifications can no longer be published.");
        }
    }
}
