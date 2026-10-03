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
    IServiceScopeFactory scopeFactory) : BackgroundService
{
    private readonly SemaphoreSlim _slots = new(configuration.MaxConcurrentMessageConsumer);
    private readonly List<Task> _inFlight = new();

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

                var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _inFlight.RemoveAll(static task => task.IsCompleted);
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

    private async Task ProcessMessageAsync(NotificationPublishContext context, TaskCompletionSource<bool> started, CancellationToken stoppingToken)
    {
        var scope = scopeFactory.CreateAsyncScope();
        var handlerTasks = new List<Task>();
        try
        {
            try
            {
                var pipelines = WrapperCache.ForNotification(context.Type).BuildHandlerPipelines(scope.ServiceProvider, context.Message);
                var policy = resiliencyProvider.GetResiliencyPolicy(context.Type);
                foreach (var pipeline in pipelines)
                {
                    await _slots.WaitAsync(stoppingToken).ConfigureAwait(false);
                    handlerTasks.Add(RunWithRetryAsync(pipeline, context.Message, policy, stoppingToken));
                }
            }
            catch (Exception ex)
            {
                deadLettersKeeper.Record(context.Message, ex, 0);
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

    private async Task RunWithRetryAsync(Func<CancellationToken, Task> pipeline, object message, NotificationRetryPolicy policy, CancellationToken stoppingToken)
    {
        var slotHeld = true;
        var attempt = 1;
        try
        {
            while (true)
            {
                try
                {
                    await pipeline(stoppingToken).ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException ex) when (stoppingToken.IsCancellationRequested)
                {
                    deadLettersKeeper.Record(message, ex, attempt);
                    return;
                }
                catch (Exception ex)
                {
                    if (attempt > policy.MaxRetryAttempts)
                    {
                        deadLettersKeeper.Record(message, ex, attempt);
                        return;
                    }

                    _slots.Release();
                    slotHeld = false;
                    try
                    {
                        if (policy.DelayBetweenRetries > TimeSpan.Zero)
                        {
                            await Task.Delay(policy.DelayBetweenRetries, stoppingToken).ConfigureAwait(false);
                        }

                        await _slots.WaitAsync(stoppingToken).ConfigureAwait(false);
                        slotHeld = true;
                    }
                    catch (OperationCanceledException)
                    {
                        deadLettersKeeper.Record(message, ex, attempt);
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

    private void DeadLetterUnread()
    {
        while (notificationChannel.TryRead(out var context))
        {
            deadLettersKeeper.Record(context.Message, new OperationCanceledException("MediatRR stopped before this notification was processed."), 0);
        }
    }
}
