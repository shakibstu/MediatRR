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
    internal sealed class HandleNotificationsWorker(NotificationChannel notificationChannel, NotificationResiliencyProvider resiliencyProvider, MediatRRConfiguration configuration, InternalDeadLettersKeeper deadLettersKeeper, IServiceProvider serviceProvider)
        : BackgroundService
    {
        private readonly SemaphoreSlim _semaphore = new(configuration.MaxConcurrentMessageConsumer);
        private const string HandlerName = nameof(INotificationHandler<>.Handle);
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
            var result = notificationChannel.ReadFromChannel(linkedCts.Token);
            var enumerator = result.GetAsyncEnumerator(linkedCts.Token);
            
            while (await enumerator.MoveNextAsync())
            {
                // Resolve the handler for this notification type
                var notificationHandlerType = typeof(INotificationHandler<>).MakeGenericType(enumerator.Current.Type);
                var handler = serviceProvider.GetService(notificationHandlerType);

                if (handler == null)
                {
                    continue; // Skip if no handler is registered
                }

                // Get notification handler behaviors for this notification type
                var behaviorType = typeof(INotificationHandlerBehavior<>).MakeGenericType(enumerator.Current.Type);
                var behaviors = serviceProvider.GetServices(behaviorType);
                var retryPolicy = resiliencyProvider.GetResiliencyPolicy(enumerator.Current.Type);

                // Build the pipeline by wrapping behaviors around the consume method
                var response = behaviors.Reverse()
                    .Aggregate(() => Consume(enumerator.Current, retryPolicy, handler, stoppingToken),
                        (next, behavior) => () =>
                            (Task)behavior.GetType()
                                .GetMethod(nameof(INotificationHandlerBehavior<>.Handle))!
                                .Invoke(behavior, [enumerator.Current.Message, next, stoppingToken])).Invoke();

                // Track the task and cleanup completed ones
                _runningTasks.TryAdd(Guid.NewGuid(), response);
                CleanupCompletedTasks();
                
                // Small delay to prevent tight loop
                await Task.Delay(TimeSpan.FromMilliseconds(100), CancellationToken.None);
            }
        }
        
        /// <summary>
        /// Removes completed tasks from the running tasks dictionary to prevent memory leaks.
        /// </summary>
        private void CleanupCompletedTasks()
        {
            var completed = _runningTasks.Where(t => t.Value.IsCompleted).ToList();
            foreach (var task in completed)
            {
                _runningTasks.TryRemove(task);
            }
        }


        /// <summary>
        /// Consumes a notification by invoking its handler with retry logic.
        /// If the handler fails after max retries, the notification is moved to the dead letter queue.
        /// </summary>
        private async Task Consume(NotificationPublishContext context, NotificationRetryPolicy retryPolicy, object handler, CancellationToken stoppingToken)
        {
            try
            {
                // Acquire semaphore to limit concurrent handler executions
                await _semaphore.WaitAsync(TimeSpan.FromSeconds(30), stoppingToken);
                
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
                _semaphore.Release();
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
            await _drainCts.CancelAsync();
            
            // Wait for ExecuteAsync to finish
            await base.StopAsync(stoppingToken);
            
            // Wait for all spawned handler tasks to complete
            await Task.WhenAll(_runningTasks.Values);
            
            _semaphore.Dispose();
            _drainCts.Dispose();
        }
    }
}
