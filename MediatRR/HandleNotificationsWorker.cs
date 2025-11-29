using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MediatRR
{
    /// <summary>
    /// Background service that processes notifications from the notification channel.
    /// Handles retry logic and dead letter queue management for failed notifications.
    /// </summary>
    internal sealed class HandleNotificationsWorker(NotificationChannel notificationChannel, NotificationResiliencyProvider resiliencyProvider, MediatRRConfiguration configuration, InternalDeadLettersKeeper deadLettersKeeper, IServiceScopeFactory scopeFactory)
        : BackgroundService
    {
        private readonly SemaphoreSlim _semaphore = new(configuration.MaxConcurrentMessageConsumer);
        private const string HandlerName = nameof(INotificationHandler<INotification>.Handle);
        private readonly ConcurrentDictionary<Guid, Task> _runningTasks = new();
        private readonly CancellationTokenSource _drainCts = new();

        /// <summary>
        /// Main execution loop that reads notifications from the channel and processes them.
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Use a linked token that combines app shutdown and our drain token
            // This allows graceful shutdown where we finish processing queued messages
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _drainCts.Token);

            while (!stoppingToken.IsCancellationRequested)
            {
                var result = await notificationChannel.ReadFromChannel(linkedCts.Token);
                // Create a scope for this message processing
                using var scope = scopeFactory.CreateScope();

                var scopedProvider = scope.ServiceProvider;

                // Resolve the handler for this notification type
                var notificationHandlerType = typeof(INotificationHandler<>).MakeGenericType(result.Type);
                var handler = scopedProvider.GetService(notificationHandlerType);

                if (handler == null)
                {
                    continue; // Skip if no handler is registered
                }

                // Get notification handler behaviors for this notification type
                var behaviorType = typeof(INotificationHandlerBehavior<>).MakeGenericType(result.Type);
                var behaviors = scopedProvider.GetServices(behaviorType);
                var retryPolicy = resiliencyProvider.GetResiliencyPolicy(result.Type);

                // Build the pipeline by wrapping behaviors around the consume method
                // We need to pass the scope to Consume so it can be disposed when the task finishes
                var response = behaviors.Reverse()
                    .Aggregate(() => Consume(result, retryPolicy, handler, stoppingToken),
                        (next, behavior) => () =>
                            (Task)behavior.GetType()
                                .GetMethod(nameof(INotificationHandlerBehavior<INotification>.Handle))!
                                .Invoke(behavior, [result.Message, next, stoppingToken])).Invoke();

                // Track the task and cleanup completed ones
                _runningTasks.TryAdd(Guid.NewGuid(), response);
                CleanupCompletedTasks();
            }
        }

        /// <summary>
        /// Removes completed tasks from the running tasks dictionary to prevent memory leaks.
        /// </summary>
        private void CleanupCompletedTasks()
        {
            // Only cleanup occasionally or if we have a lot of tasks to avoid iterating too often
            if (_runningTasks.Count > 100)
            {
                var completed = _runningTasks.Where(t => t.Value.IsCompleted).Select(t => t.Key).ToList();
                foreach (var key in completed)
                {
                    _runningTasks.TryRemove(key, out _);
                }
            }
        }


        /// <summary>
        /// Consumes a notification by invoking its handler with retry logic.
        /// If the handler fails after max retries, the notification is moved to the dead letter queue.
        /// </summary>
        private async Task Consume(NotificationPublishContext context, NotificationRetryPolicy retryPolicy, object handler, CancellationToken stoppingToken)
        {
            var semaphoreAcquired = false;
            try
            {
                // Acquire semaphore to limit concurrent handler executions
                if (!await _semaphore.WaitAsync(TimeSpan.FromMinutes(1), stoppingToken))
                {
                    // Timeout acquiring semaphore. 
                    // We treat this as a failure to process, so we can retry or DLQ.
                    throw new TimeoutException("Timed out waiting for concurrency semaphore");
                }
                semaphoreAcquired = true;

                // Invoke the handler's Handle method using reflection
                await ((Task)handler!.GetType().GetMethod(HandlerName)!.Invoke(handler,
                    [context.Message, stoppingToken]))!;
            }
            catch (Exception ex)
            {
                // Check if we've exceeded max retry attempts
                if (context.RetriedCount >= retryPolicy.MaxRetryAttempts)
                {
                    // Move to dead letter queue
                    deadLettersKeeper.DeadLettersQueue?.Enqueue(
                        new DeadLettersInfo(context.Message, ex, context.RetriedCount, DateTime.UtcNow));
                    return;
                }

                // Increment retry count and re-queue the notification
                context.IncreaseRetry();
                await Task.Delay(retryPolicy.DelayBetweenRetries, stoppingToken);
                await notificationChannel.AddToChannel(context, stoppingToken);
            }
            finally
            {
                if (semaphoreAcquired)
                {
                    _semaphore.Release();
                }
            }
        }

        /// <summary>
        /// Gracefully stops the worker by ensuring all queued notifications are processed.
        /// </summary>
        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            // Stop accepting new messages
            notificationChannel.Stop();

            // Wait for channel to be drained (all messages picked up by ExecuteAsync)
            while (notificationChannel.Count > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), stoppingToken);
            }

            // Now cancel the drain token to stop the ExecuteAsync loop
            _drainCts.Cancel();

            // Wait for ExecuteAsync to finish
            await base.StopAsync(stoppingToken);

            // Wait for all spawned handler tasks to complete
            await Task.WhenAll(_runningTasks.Values);

            _semaphore.Dispose();
            _drainCts.Dispose();
        }
    }
}
