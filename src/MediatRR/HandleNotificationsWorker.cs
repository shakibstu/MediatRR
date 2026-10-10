using MediatRR.Contract.Messaging;
using MediatRR.Dispatch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace MediatRR;

/// <summary>
/// Background service that processes notifications from the notification channel.
/// </summary>
internal sealed class HandleNotificationsWorker(
    NotificationChannel notificationChannel,
    NotificationResiliencyProvider resiliencyProvider,
    MediatRRConfiguration configuration,
    InternalDeadLettersKeeper deadLettersKeeper,
    MediatRRMetrics metrics,
    IServiceScopeFactory scopeFactory) : BackgroundService
{
    private readonly SemaphoreSlim _slots = new(configuration.MaxConcurrentMessageConsumer);
    private readonly SemaphoreSlim _orderedBacklog = new(configuration.NotificationChannelSize);
    private readonly List<Task> _inFlight = new();
    private readonly Dictionary<string, Task> _orderedTails = new();
    private readonly object _orderedLock = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
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
                    break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                _inFlight.RemoveAll(static task => task.IsCompleted);
                if ((context.Message as IOrderedNotification)?.OrderingKey is { } orderingKey)
                {
                    try
                    {
                        await _orderedBacklog.WaitAsync(stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException ex)
                    {
                        deadLettersKeeper.Record(context.Message, ex, 0);
                        break;
                    }

                    _inFlight.Add(ProcessOrderedAsync(orderingKey, context, stoppingToken));
                    continue;
                }

                var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _inFlight.Add(ProcessMessageAsync(context, started, stoppingToken));
                await started.Task.ConfigureAwait(false);
            }
        }
        finally
        {
            DeadLetterUnread();
            await Task.WhenAll(_inFlight).ConfigureAwait(false);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        notificationChannel.Stop();
        if (ExecuteTask is { } executeTask)
        {
            await Task.WhenAny(executeTask, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task ProcessOrderedAsync(string orderingKey, NotificationPublishContext context, CancellationToken stoppingToken)
    {
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task previous;
        lock (_orderedLock)
        {
            _orderedTails.TryGetValue(orderingKey, out previous);
            _orderedTails[orderingKey] = done.Task;
        }

        return RunOrderedAsync(previous, done, orderingKey, context, stoppingToken);
    }

    private async Task RunOrderedAsync(Task previous, TaskCompletionSource<bool> done, string orderingKey, NotificationPublishContext context, CancellationToken stoppingToken)
    {
        try
        {
            if (previous is not null)
            {
                await previous.ConfigureAwait(false);
            }

            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await ProcessMessageAsync(context, started, stoppingToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_orderedLock)
            {
                if (_orderedTails.TryGetValue(orderingKey, out var tail) && ReferenceEquals(tail, done.Task))
                {
                    _orderedTails.Remove(orderingKey);
                }
            }

            done.TrySetResult(true);
            _orderedBacklog.Release();
        }
    }

    private async Task ProcessMessageAsync(NotificationPublishContext context, TaskCompletionSource<bool> started, CancellationToken stoppingToken)
    {
        var scope = scopeFactory.CreateAsyncScope();
        var handlerTasks = new List<Task>();
        try
        {
            NotificationWrapper wrapper = null;
            try
            {
                wrapper = WrapperCache.ForNotification(context.Type);
                var pipelines = wrapper.BuildHandlerPipelines(scope.ServiceProvider, context.Message);
                var policy = resiliencyProvider.GetResiliencyPolicy(context.Type);
                foreach (var pipeline in pipelines)
                {
                    await _slots.WaitAsync(stoppingToken).ConfigureAwait(false);
                    handlerTasks.Add(RunWithRetryAsync(wrapper, scope.ServiceProvider, pipeline, context.Message, policy, stoppingToken));
                }
            }
            catch (Exception ex)
            {
                handlerTasks.Add(DeadLetterAsync(wrapper, scope.ServiceProvider, context.Message, ex, 0, null, stoppingToken));
            }
            finally
            {
                started.TrySetResult(true);
            }

            await Task.WhenAll(handlerTasks).ConfigureAwait(false);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task RunWithRetryAsync(NotificationWrapper wrapper, IServiceProvider serviceProvider, HandlerPipeline pipeline, INotification message, NotificationRetryPolicy policy, CancellationToken stoppingToken)
    {
        var slotHeld = true;
        var attempt = 1;
        var notificationType = message.GetType();
        try
        {
            while (true)
            {
                var startedAt = metrics.HandlerStarted();
                try
                {
                    await pipeline.Invoke(stoppingToken).ConfigureAwait(false);
                    metrics.HandlerFinished(startedAt, notificationType, pipeline.HandlerType, "success");
                    return;
                }
                catch (OperationCanceledException ex) when (stoppingToken.IsCancellationRequested)
                {
                    metrics.HandlerFinished(startedAt, notificationType, pipeline.HandlerType, "cancelled");
                    deadLettersKeeper.Record(message, ex, attempt, pipeline.HandlerType);
                    return;
                }
                catch (Exception ex)
                {
                    metrics.HandlerFinished(startedAt, notificationType, pipeline.HandlerType, "failure");
                    if (attempt > policy.MaxRetryAttempts || !policy.IsRetryable(ex))
                    {
                        await DeadLetterAsync(wrapper, serviceProvider, message, ex, attempt, pipeline.HandlerType, stoppingToken).ConfigureAwait(false);
                        return;
                    }

                    metrics.HandlerRetried(notificationType, pipeline.HandlerType);
                    _slots.Release();
                    slotHeld = false;
                    try
                    {
                        var delay = policy.GetRetryDelay(attempt);
                        if (delay > TimeSpan.Zero)
                        {
                            await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
                        }

                        await _slots.WaitAsync(stoppingToken).ConfigureAwait(false);
                        slotHeld = true;
                    }
                    catch (OperationCanceledException)
                    {
                        deadLettersKeeper.Record(message, ex, attempt, pipeline.HandlerType);
                        return;
                    }

                    attempt++;
                }
            }
        }
        finally
        {
            if (slotHeld)
            {
                _slots.Release();
            }
        }
    }

    private async Task DeadLetterAsync(NotificationWrapper wrapper, IServiceProvider serviceProvider, INotification message, Exception exception, int attempt, Type handlerType, CancellationToken stoppingToken)
    {
        var deadLetter = deadLettersKeeper.Record(message, exception, attempt, handlerType);
        if (wrapper is null || stoppingToken.IsCancellationRequested)
        {
            return;
        }

        IReadOnlyList<HandlerPipeline> pipelines;
        try
        {
            pipelines = wrapper.BuildDeadLetterPipelines(serviceProvider, message, deadLetter);
        }
        catch (Exception ex)
        {
            deadLettersKeeper.Record(message, ex, 0);
            return;
        }

        foreach (var pipeline in pipelines)
        {
            try
            {
                await pipeline.Invoke(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                deadLettersKeeper.Record(message, ex, 1, pipeline.HandlerType);
            }
        }
    }

    private void DeadLetterUnread()
    {
        while (notificationChannel.TryRead(out var context))
        {
            deadLettersKeeper.Record(context.Message, new OperationCanceledException("MediatRR stopped before this notification was processed."), 0);
        }
    }
}
