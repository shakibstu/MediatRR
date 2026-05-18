using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace MediatRR
{
    /// <summary>
    /// Background service that processes notifications from the notification channel.
    /// </summary>
    internal sealed class HandleNotificationsWorker(
        NotificationChannel notificationChannel,
        NotificationResiliencyProvider resiliencyProvider,
        MediatRRConfiguration configuration,
        InternalDeadLettersKeeper deadLettersKeeper,
        IServiceScopeFactory scopeFactory) : BackgroundService
    {
        private readonly SemaphoreSlim _semaphore = new(configuration.MaxConcurrentMessageConsumer);
        private readonly ConcurrentDictionary<Guid, Task> _runningTasks = new();

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (true)
            {
                NotificationPublishContext context;
                try
                {
                    context = await notificationChannel.ReadFromChannel(stoppingToken).ConfigureAwait(false);
                }
                catch (ChannelClosedException)
                {
                    // Writer was completed and the channel is drained — clean exit.
                    break;
                }
                catch (OperationCanceledException)
                {
                    // Forced shutdown via stoppingToken — exit without further drain.
                    break;
                }

                DispatchToHandlers(context, stoppingToken);
            }
        }

        private void DispatchToHandlers(NotificationPublishContext context, CancellationToken stoppingToken)
        {
            using var scope = scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;

            var handlers = sp.GetServices(HandlerCache.NotificationHandlerType(context.Type)).ToList();
            if (handlers.Count == 0) return;

            // Snapshot behaviors and pre-reverse ONCE before fanning out to handlers.
            var behaviorsReversed = sp.GetServices(HandlerCache.NotificationHandlerBehaviorType(context.Type))
                .Reverse()
                .ToList();
            var retryPolicy = resiliencyProvider.GetResiliencyPolicy(context.Type);

            foreach (var handler in handlers)
            {
                var pipeline = behaviorsReversed
                    .Aggregate((Func<Task>)(() => Consume(context, retryPolicy, handler, stoppingToken)),
                        (next, behavior) => () => HandlerCache.InvokeNotificationHandlerBehavior(behavior, context.Message, next, stoppingToken));

                Track(pipeline());
            }
        }

        private void Track(Task task)
        {
            var id = Guid.NewGuid();
            _runningTasks[id] = task;
            task.ContinueWith((t, state) =>
            {
                var (dict, key, dlq) = ((ConcurrentDictionary<Guid, Task>, Guid, InternalDeadLettersKeeper))state;
                dict.TryRemove(key, out _);
                if (t.IsFaulted)
                {
                    // Behavior threw outside Consume's catch; capture so it isn't unobserved.
                    dlq.DeadLettersQueue.Enqueue(
                        new DeadLettersInfo(null, t.Exception.GetBaseException(), 0, DateTime.UtcNow));
                }
            }, (_runningTasks, id, deadLettersKeeper), TaskContinuationOptions.ExecuteSynchronously);
        }

        private async Task Consume(NotificationPublishContext context, NotificationRetryPolicy retryPolicy, object handler, CancellationToken stoppingToken)
        {
            var semaphoreAcquired = false;
            try
            {
                if (!await _semaphore.WaitAsync(TimeSpan.FromMinutes(1), stoppingToken).ConfigureAwait(false))
                {
                    throw new TimeoutException("Timed out waiting for concurrency semaphore");
                }
                semaphoreAcquired = true;

                await HandlerCache.InvokeNotificationHandler(handler, context.Message, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Release the semaphore BEFORE retry delay / re-enqueue so we don't starve other work.
                if (semaphoreAcquired)
                {
                    _semaphore.Release();
                    semaphoreAcquired = false;
                }

                if (context.RetriedCount >= retryPolicy.MaxRetryAttempts)
                {
                    deadLettersKeeper.DeadLettersQueue.Enqueue(
                        new DeadLettersInfo(context.Message, ex, context.RetriedCount, DateTime.UtcNow));
                    return;
                }

                context.IncreaseRetry();
                if (retryPolicy.DelayBetweenRetries > TimeSpan.Zero)
                {
                    await Task.Delay(retryPolicy.DelayBetweenRetries, stoppingToken).ConfigureAwait(false);
                }
                await notificationChannel.AddToChannel(context, stoppingToken).ConfigureAwait(false);
            }
            finally
            {
                if (semaphoreAcquired)
                {
                    _semaphore.Release();
                }
            }
        }

        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            // Stop accepting new messages.
            notificationChannel.Stop();

            // Wait for the channel to drain naturally — ExecuteAsync keeps reading until the
            // writer is completed AND the channel is empty. Channel.Reader.Completion captures
            // that exact moment. We must wait for this BEFORE base.StopAsync cancels the
            // stoppingToken, otherwise unread items get dropped.
            try
            {
                await notificationChannel.Completion.ConfigureAwait(false);
            }
            catch
            {
                // Completion can complete with an exception if writer.Complete(error) was used;
                // we don't pass an error so this is defensive.
            }

            // Drain spawned handler tasks while the stoppingToken is still live so handlers
            // can finish without being aborted.
            try
            {
                await Task.WhenAll(_runningTasks.Values).ConfigureAwait(false);
            }
            catch
            {
                // Individual task failures already routed to DLQ.
            }

            // Now formally tear down the BackgroundService (cancels stoppingToken).
            await base.StopAsync(stoppingToken).ConfigureAwait(false);

            _semaphore.Dispose();
        }
    }
}
