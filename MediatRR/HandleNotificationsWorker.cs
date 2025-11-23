using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MediatRR
{
    internal class HandleNotificationsWorker(NotificationChannel notificationChannel,ConcurrentDictionary<Type, NotificationRetryPolicy> notificationRetryPolicies, IServiceProvider serviceProvider)
        : BackgroundService
    {
        private const string HandlerName = nameof(INotificationHandler<INotification>.Handle);
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var result = notificationChannel.ReadFromChannel(stoppingToken);
            var enumerator = result.GetAsyncEnumerator(stoppingToken);
            while (await enumerator.MoveNextAsync())
            {
                var notificationHandlerType = typeof(INotificationHandler<>).MakeGenericType(enumerator.Current.Type);
                var handler = serviceProvider.GetService(notificationHandlerType);

                if (handler == null)
                {
                    continue;
                }

                var behaviorType = typeof(INotificationHandlerBehavior<>).MakeGenericType(enumerator.Current.Type);

                var behaviors = serviceProvider.GetServices(behaviorType);
                var retryPolicy = notificationRetryPolicies[enumerator.Current.Type];
                var response = behaviors.Reverse()
                    .Aggregate(() => Consume(enumerator.Current, retryPolicy, handler, stoppingToken),
                        (next, behavior) => () =>
                            (Task)behavior.GetType()
                                .GetMethod(nameof(INotificationHandlerBehavior<INotification>.Handle))!
                                .Invoke(behavior, [enumerator.Current.Message, next, stoppingToken])).Invoke();
                await response;
                await Task.Delay(TimeSpan.FromMilliseconds(100), stoppingToken);
            }
        }

        private async Task Consume(NotificationPublishContext context, NotificationRetryPolicy retryPolicy, object handler, CancellationToken stoppingToken)
        {
            try
            {
                await ((Task)handler!.GetType().GetMethod(HandlerName)!.Invoke(handler, [context.Message, stoppingToken]))!;
            }
            catch (Exception)
            {
                if (context.RetriedCount >= retryPolicy.MaxRetryAttempts)
                {
                    return;
                }
                context.RetriedCount += 1;
                await Task.Delay(retryPolicy.DelayBetweenRetries, stoppingToken);
                await notificationChannel.AddToChannel(context, stoppingToken);
            }
        }
        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            notificationChannel.Stop();

            while (notificationChannel.Count > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), stoppingToken);
            }
            await base.StopAsync(stoppingToken);
        }
    }
}
