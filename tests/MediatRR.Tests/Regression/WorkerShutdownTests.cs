using System.Collections.Concurrent;
using System.Diagnostics;
using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace MediatRR.Tests.Regression;

public class WorkerShutdownTests
{
    public sealed class Note : INotification { }

    public sealed class NoteHandler : INotificationHandler<Note>
    {
        public Task Handle(Note notification, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class Retrying : INotification { }

    public sealed class RetryingHandler : INotificationHandler<Retrying>
    {
        public static int Runs;

        public Task Handle(Retrying notification, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Runs);
            throw new InvalidOperationException("retry me");
        }
    }

    public sealed class Slow : INotification { }

    public sealed class CooperativeSlowHandler : INotificationHandler<Slow>
    {
        public Task Handle(Slow notification, CancellationToken cancellationToken) => Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
    }

    public sealed class Queued : INotification { }

    public sealed class CooperativeQueuedHandler : INotificationHandler<Queued>
    {
        public Task Handle(Queued notification, CancellationToken cancellationToken) => Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
    }

    [Fact]
    public async Task Publish_after_StopAsync_throws_InvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddMediatRR(_ => { }, new ConcurrentQueue<DeadLettersInfo>());
        services.AddNotificationHandler<Note, NoteHandler>();
        var (_, worker, mediator) = await WorkerHost.StartAsync(services);

        await worker.StopAsync(CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Publish(new Note()));
    }

    [Fact]
    public async Task Graceful_stop_waits_for_a_pending_retry_and_records_the_real_failure()
    {
        var deadLetters = new ConcurrentQueue<DeadLettersInfo>();
        var services = new ServiceCollection();
        services.AddMediatRR(_ => { }, deadLetters);
        services.AddNotificationHandler<Retrying, RetryingHandler>(new NotificationRetryPolicy { MaxRetryAttempts = 1, DelayBetweenRetries = TimeSpan.FromMilliseconds(300) });
        var (_, worker, mediator) = await WorkerHost.StartAsync(services);

        await mediator.Publish(new Retrying());
        await WorkerHost.WaitUntilAsync(() => RetryingHandler.Runs >= 1, TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(2, RetryingHandler.Runs);
        var deadLetter = Assert.Single(deadLetters);
        Assert.IsType<Retrying>(deadLetter.Message);
        Assert.IsType<InvalidOperationException>(deadLetter.Exception);
        Assert.Equal(2, deadLetter.AttemptCount);
    }

    [Fact]
    public async Task Forced_stop_returns_when_its_token_fires_and_dead_letters_the_abandoned_handler()
    {
        var deadLetters = new ConcurrentQueue<DeadLettersInfo>();
        var services = new ServiceCollection();
        services.AddMediatRR(_ => { }, deadLetters);
        services.AddNotificationHandler<Slow, CooperativeSlowHandler>();
        var (_, worker, mediator) = await WorkerHost.StartAsync(services);

        await mediator.Publish(new Slow());
        await Task.Delay(100);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var stopwatch = Stopwatch.StartNew();
        await worker.StopAsync(cts.Token);
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3), $"StopAsync took {stopwatch.Elapsed}");
        await WorkerHost.WaitUntilAsync(() => !deadLetters.IsEmpty, TimeSpan.FromSeconds(2));
        var deadLetter = Assert.Single(deadLetters);
        Assert.IsType<Slow>(deadLetter.Message);
        Assert.IsAssignableFrom<OperationCanceledException>(deadLetter.Exception);
    }

    [Fact]
    public async Task Forced_stop_dead_letters_notifications_still_waiting_for_a_slot_or_in_the_channel()
    {
        var deadLetters = new ConcurrentQueue<DeadLettersInfo>();
        var services = new ServiceCollection();
        services.AddMediatRR(cfg => cfg.MaxConcurrentMessageConsumer = 1, deadLetters);
        services.AddNotificationHandler<Queued, CooperativeQueuedHandler>();
        var (_, worker, mediator) = await WorkerHost.StartAsync(services);

        await mediator.Publish(new Queued());
        await mediator.Publish(new Queued());
        await mediator.Publish(new Queued());
        await Task.Delay(100);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await worker.StopAsync(cts.Token);

        await WorkerHost.WaitUntilAsync(() => deadLetters.Count == 3, TimeSpan.FromSeconds(2));
        Assert.Equal(3, deadLetters.Count);
        Assert.All(deadLetters, deadLetter =>
        {
            Assert.IsType<Queued>(deadLetter.Message);
            Assert.IsAssignableFrom<OperationCanceledException>(deadLetter.Exception);
        });
    }
}
