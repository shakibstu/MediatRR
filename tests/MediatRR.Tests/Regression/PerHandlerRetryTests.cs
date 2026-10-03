using System.Collections.Concurrent;
using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace MediatRR.Tests.Regression;

public class PerHandlerRetryTests
{
    private static readonly NotificationRetryPolicy TwoRetries = new() { MaxRetryAttempts = 2, DelayBetweenRetries = TimeSpan.FromMilliseconds(10) };

    public sealed class Note : INotification { }

    public sealed class GoodHandler : INotificationHandler<Note>
    {
        public static int Runs;

        public Task Handle(Note notification, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Runs);
            return Task.CompletedTask;
        }
    }

    public sealed class BadHandler : INotificationHandler<Note>
    {
        public static int Runs;

        public Task Handle(Note notification, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Runs);
            throw new InvalidOperationException("bad");
        }
    }

    public sealed class Other : INotification { }

    public sealed class Fail1 : INotificationHandler<Other>
    {
        public static int Runs;

        public Task Handle(Other notification, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Runs);
            throw new InvalidOperationException("f1");
        }
    }

    public sealed class Fail2 : INotificationHandler<Other>
    {
        public static int Runs;

        public Task Handle(Other notification, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Runs);
            throw new InvalidOperationException("f2");
        }
    }

    public sealed class Wrapped : INotification { }

    public sealed class WrappedHandler : INotificationHandler<Wrapped>
    {
        public Task Handle(Wrapped notification, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class ThrowingBehavior : INotificationHandlerBehavior<Wrapped>
    {
        public static int Runs;

        public async Task Handle(Wrapped request, Func<Task> next, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Runs);
            await Task.Yield();
            throw new InvalidOperationException("behavior");
        }
    }

    [Fact]
    public async Task Retrying_a_failing_handler_does_not_rerun_a_handler_that_succeeded()
    {
        var deadLetters = new ConcurrentQueue<DeadLettersInfo>();
        var services = new ServiceCollection();
        services.AddMediatRR(_ => { }, deadLetters);
        services.AddNotificationHandler<Note, GoodHandler>(TwoRetries);
        services.AddNotificationHandler<Note, BadHandler>(TwoRetries);
        var (_, worker, mediator) = await WorkerHost.StartAsync(services);

        await mediator.Publish(new Note());
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(1, GoodHandler.Runs);
        Assert.Equal(3, BadHandler.Runs);
        var deadLetter = Assert.Single(deadLetters);
        Assert.IsType<Note>(deadLetter.Message);
        Assert.IsType<InvalidOperationException>(deadLetter.Exception);
        Assert.Equal(3, deadLetter.AttemptCount);
    }

    [Fact]
    public async Task Each_failing_handler_has_its_own_retry_budget_and_one_dead_letter()
    {
        var deadLetters = new ConcurrentQueue<DeadLettersInfo>();
        var services = new ServiceCollection();
        services.AddMediatRR(_ => { }, deadLetters);
        services.AddNotificationHandler<Other, Fail1>(TwoRetries);
        services.AddNotificationHandler<Other, Fail2>(TwoRetries);
        var (_, worker, mediator) = await WorkerHost.StartAsync(services);

        await mediator.Publish(new Other());
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(2, deadLetters.Count);
        Assert.Equal(3, Fail1.Runs);
        Assert.Equal(3, Fail2.Runs);
        Assert.All(deadLetters, deadLetter => Assert.Equal(3, deadLetter.AttemptCount));
    }

    [Fact]
    public async Task A_failing_handler_behavior_is_retried_and_dead_lettered_with_the_message()
    {
        var deadLetters = new ConcurrentQueue<DeadLettersInfo>();
        var services = new ServiceCollection();
        services.AddMediatRR(_ => { }, deadLetters);
        services.AddNotificationHandler<Wrapped, WrappedHandler>(new NotificationRetryPolicy { MaxRetryAttempts = 1 });
        services.AddTransient<INotificationHandlerBehavior<Wrapped>, ThrowingBehavior>();
        var (_, worker, mediator) = await WorkerHost.StartAsync(services);

        await mediator.Publish(new Wrapped());
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(2, ThrowingBehavior.Runs);
        var deadLetter = Assert.Single(deadLetters);
        Assert.IsType<Wrapped>(deadLetter.Message);
        Assert.Equal("behavior", deadLetter.Exception.Message);
        Assert.Equal(2, deadLetter.AttemptCount);
    }
}
