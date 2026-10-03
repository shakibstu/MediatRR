using System.Collections.Concurrent;
using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace MediatRR.Tests.Regression;

public class BackpressureTests
{
    public sealed class Gate
    {
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public sealed class Note : INotification { }

    public sealed class BlockingHandler(Gate gate) : INotificationHandler<Note>
    {
        public Task Handle(Note notification, CancellationToken cancellationToken) => gate.Release.Task.WaitAsync(cancellationToken);
    }

    [Fact]
    public async Task Publish_blocks_once_the_channel_and_all_consumer_slots_are_full()
    {
        var gate = new Gate();
        var deadLetters = new ConcurrentQueue<DeadLettersInfo>();
        var services = new ServiceCollection();
        services.AddSingleton(gate);
        services.AddMediatRR(cfg =>
        {
            cfg.NotificationChannelSize = 2;
            cfg.MaxConcurrentMessageConsumer = 1;
        }, deadLetters);
        services.AddNotificationHandler<Note, BlockingHandler>();
        var (_, worker, mediator) = await WorkerHost.StartAsync(services);

        for (var i = 0; i < 4; i++)
        {
            await mediator.Publish(new Note());
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mediator.Publish(new Note(), cts.Token));

        gate.Release.SetResult(true);
        await worker.StopAsync(CancellationToken.None);
        Assert.Empty(deadLetters);
    }
}
