using System.Collections.Concurrent;
using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace MediatRR.Tests.Regression;

public class WorkerResilienceTests
{
    public interface IMissing { }

    public sealed class Broken : INotification { }

    public sealed class NeedsMissing : INotificationHandler<Broken>
    {
        private readonly IMissing _missing;

        public NeedsMissing(IMissing missing) => _missing = missing;

        public Task Handle(Broken notification, CancellationToken cancellationToken) => Task.FromResult(_missing);
    }

    public sealed class Healthy : INotificationHandler<Broken>
    {
        public Task Handle(Broken notification, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class Fine : INotification { }

    public sealed class FineHandler : INotificationHandler<Fine>
    {
        public static volatile bool Ran;

        public Task Handle(Fine notification, CancellationToken cancellationToken)
        {
            Ran = true;
            return Task.CompletedTask;
        }
    }

    public sealed class Wrapped : INotification { }

    public sealed class WrappedHandler : INotificationHandler<Wrapped>
    {
        public Task Handle(Wrapped notification, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class SyncThrowingBehavior : INotificationHandlerBehavior<Wrapped>
    {
        public Task Handle(Wrapped request, Func<Task> next, CancellationToken cancellationToken = default) => throw new InvalidOperationException("sync behavior");
    }

    public sealed class Later : INotification { }

    public sealed class LaterHandler : INotificationHandler<Later>
    {
        public static volatile bool Ran;

        public Task Handle(Later notification, CancellationToken cancellationToken)
        {
            Ran = true;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task A_handler_that_cannot_be_resolved_is_dead_lettered_and_the_worker_keeps_running()
    {
        var deadLetters = new ConcurrentQueue<DeadLettersInfo>();
        var services = new ServiceCollection();
        services.AddMediatRR(_ => { }, deadLetters);
        services.AddNotificationHandler<Broken, NeedsMissing>();
        services.AddNotificationHandler<Broken, Healthy>();
        services.AddNotificationHandler<Fine, FineHandler>();
        var (_, worker, mediator) = await WorkerHost.StartAsync(services);

        await mediator.Publish(new Broken());
        await mediator.Publish(new Fine());
        await worker.StopAsync(CancellationToken.None);

        Assert.False(worker.ExecuteTask!.IsFaulted);
        Assert.True(FineHandler.Ran);
        var deadLetter = Assert.Single(deadLetters);
        Assert.IsType<Broken>(deadLetter.Message);
        Assert.IsType<InvalidOperationException>(deadLetter.Exception);
    }

    [Fact]
    public async Task A_synchronously_throwing_behavior_is_dead_lettered_and_the_worker_keeps_running()
    {
        var deadLetters = new ConcurrentQueue<DeadLettersInfo>();
        var services = new ServiceCollection();
        services.AddMediatRR(_ => { }, deadLetters);
        services.AddNotificationHandler<Wrapped, WrappedHandler>();
        services.AddTransient<INotificationHandlerBehavior<Wrapped>, SyncThrowingBehavior>();
        services.AddNotificationHandler<Later, LaterHandler>();
        var (_, worker, mediator) = await WorkerHost.StartAsync(services);

        await mediator.Publish(new Wrapped());
        await mediator.Publish(new Later());
        await worker.StopAsync(CancellationToken.None);

        Assert.False(worker.ExecuteTask!.IsFaulted);
        Assert.True(LaterHandler.Ran);
        var deadLetter = Assert.Single(deadLetters);
        Assert.IsType<Wrapped>(deadLetter.Message);
        Assert.Equal("sync behavior", deadLetter.Exception.Message);
        Assert.Equal(1, deadLetter.AttemptCount);
    }
}
