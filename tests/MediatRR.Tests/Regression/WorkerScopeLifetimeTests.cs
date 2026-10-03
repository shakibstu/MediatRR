using System.Collections.Concurrent;
using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace MediatRR.Tests.Regression;

public class WorkerScopeLifetimeTests
{
    public sealed class ScopedDependency : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;

        public void Touch()
        {
            if (Disposed) throw new ObjectDisposedException(nameof(ScopedDependency));
        }
    }

    public sealed class Note : INotification { }

    public sealed class AwaitingHandler(ScopedDependency dependency) : INotificationHandler<Note>
    {
        public static volatile bool Ran;

        public async Task Handle(Note notification, CancellationToken cancellationToken)
        {
            await Task.Delay(100, cancellationToken);
            dependency.Touch();
            Ran = true;
        }
    }

    [Fact]
    public async Task Scoped_dependency_stays_alive_until_the_handler_completes()
    {
        var deadLetters = new ConcurrentQueue<DeadLettersInfo>();
        var services = new ServiceCollection();
        services.AddScoped<ScopedDependency>();
        services.AddMediatRR(_ => { }, deadLetters);
        services.AddNotificationHandler<Note, AwaitingHandler>();
        var (_, worker, mediator) = await WorkerHost.StartAsync(services);

        await mediator.Publish(new Note());
        await worker.StopAsync(CancellationToken.None);

        Assert.True(AwaitingHandler.Ran);
        Assert.Empty(deadLetters);
    }
}
