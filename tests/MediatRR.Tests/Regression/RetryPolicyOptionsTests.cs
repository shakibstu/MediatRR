using System.Collections.Concurrent;
using System.Diagnostics;
using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace MediatRR.Tests.Regression;

public class RetryPolicyOptionsTests
{
    public sealed class Note : INotification { }

    public sealed class ThrowsInvalidOperation : INotificationHandler<Note>
    {
        public static int Runs;

        public Task Handle(Note notification, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Runs);
            throw new InvalidOperationException("permanent");
        }
    }

    public sealed class ThrowsTimeout : INotificationHandler<Note>
    {
        public static int Runs;

        public Task Handle(Note notification, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Runs);
            throw new TimeoutException("transient");
        }
    }

    public sealed class Other : INotification { }

    public sealed class Flaky : INotificationHandler<Other>
    {
        public static int Runs;

        public Task Handle(Other notification, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Runs);
            throw new TimeoutException("flaky");
        }
    }

    [Fact]
    public async Task An_exception_rejected_by_ShouldRetry_is_dead_lettered_without_retrying()
    {
        ThrowsInvalidOperation.Runs = 0;
        var deadLetters = new ConcurrentQueue<DeadLettersInfo>();
        var services = new ServiceCollection();
        services.AddMediatRR(_ => { }, deadLetters);
        services.AddNotificationHandler<Note, ThrowsInvalidOperation>(new NotificationRetryPolicy
        {
            MaxRetryAttempts = 3,
            ShouldRetry = exception => exception is TimeoutException
        });
        var (_, worker, mediator) = await WorkerHost.StartAsync(services);

        await mediator.Publish(new Note());
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(1, ThrowsInvalidOperation.Runs);
        var deadLetter = Assert.Single(deadLetters);
        Assert.Equal(1, deadLetter.AttemptCount);
        Assert.IsType<InvalidOperationException>(deadLetter.Exception);
    }

    [Fact]
    public async Task An_exception_accepted_by_ShouldRetry_is_retried_up_to_the_budget()
    {
        ThrowsTimeout.Runs = 0;
        var deadLetters = new ConcurrentQueue<DeadLettersInfo>();
        var services = new ServiceCollection();
        services.AddMediatRR(_ => { }, deadLetters);
        services.AddNotificationHandler<Note, ThrowsTimeout>(new NotificationRetryPolicy
        {
            MaxRetryAttempts = 3,
            ShouldRetry = exception => exception is TimeoutException
        });
        var (_, worker, mediator) = await WorkerHost.StartAsync(services);

        await mediator.Publish(new Note());
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(4, ThrowsTimeout.Runs);
        Assert.Equal(4, Assert.Single(deadLetters).AttemptCount);
    }

    [Fact]
    public void Backoff_multiplier_grows_the_delay_per_attempt_and_the_cap_limits_it()
    {
        var policy = new NotificationRetryPolicy
        {
            MaxRetryAttempts = 5,
            DelayBetweenRetries = TimeSpan.FromMilliseconds(100),
            BackoffMultiplier = 2,
            MaxDelayBetweenRetries = TimeSpan.FromMilliseconds(300)
        };

        Assert.Equal(TimeSpan.FromMilliseconds(100), policy.GetRetryDelay(1));
        Assert.Equal(TimeSpan.FromMilliseconds(200), policy.GetRetryDelay(2));
        Assert.Equal(TimeSpan.FromMilliseconds(300), policy.GetRetryDelay(3));
        Assert.Equal(TimeSpan.FromMilliseconds(300), policy.GetRetryDelay(4));
    }

    [Fact]
    public void Default_policy_keeps_a_constant_delay()
    {
        var policy = new NotificationRetryPolicy { MaxRetryAttempts = 3, DelayBetweenRetries = TimeSpan.FromMilliseconds(50) };

        Assert.Equal(TimeSpan.FromMilliseconds(50), policy.GetRetryDelay(1));
        Assert.Equal(TimeSpan.FromMilliseconds(50), policy.GetRetryDelay(3));
    }

    [Fact]
    public async Task The_worker_waits_the_backed_off_delay_between_attempts()
    {
        Flaky.Runs = 0;
        var services = new ServiceCollection();
        services.AddMediatRR(_ => { }, new ConcurrentQueue<DeadLettersInfo>());
        services.AddNotificationHandler<Other, Flaky>(new NotificationRetryPolicy
        {
            MaxRetryAttempts = 2,
            DelayBetweenRetries = TimeSpan.FromMilliseconds(50),
            BackoffMultiplier = 4
        });
        var (_, worker, mediator) = await WorkerHost.StartAsync(services);

        var stopwatch = Stopwatch.StartNew();
        await mediator.Publish(new Other());
        await worker.StopAsync(CancellationToken.None);
        stopwatch.Stop();

        Assert.Equal(3, Flaky.Runs);
        Assert.True(stopwatch.ElapsedMilliseconds >= 240, $"elapsed {stopwatch.ElapsedMilliseconds}ms, expected at least 50 + 200");
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_backoff_multiplier_below_one_is_rejected(double multiplier)
    {
        var services = new ServiceCollection();
        services.AddMediatRR(_ => { }, new ConcurrentQueue<DeadLettersInfo>());

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            services.AddNotificationHandler<Note, ThrowsTimeout>(new NotificationRetryPolicy { BackoffMultiplier = multiplier }));
    }

    [Fact]
    public void A_negative_max_delay_is_rejected()
    {
        var services = new ServiceCollection();
        services.AddMediatRR(_ => { }, new ConcurrentQueue<DeadLettersInfo>());

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            services.AddNotificationHandler<Note, ThrowsTimeout>(new NotificationRetryPolicy { MaxDelayBetweenRetries = TimeSpan.FromSeconds(-1) }));
    }

    [Fact]
    public void Policies_that_differ_only_in_ShouldRetry_conflict()
    {
        var services = new ServiceCollection();
        services.AddMediatRR(_ => { }, new ConcurrentQueue<DeadLettersInfo>());
        services.AddNotificationHandler<Note, ThrowsTimeout>(new NotificationRetryPolicy { MaxRetryAttempts = 2, ShouldRetry = _ => true });
        services.AddNotificationHandler<Note, ThrowsInvalidOperation>(new NotificationRetryPolicy { MaxRetryAttempts = 2, ShouldRetry = _ => false });

        var provider = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<NotificationResiliencyProvider>());
    }

    [Fact]
    public void Policies_that_differ_only_in_backoff_conflict()
    {
        var services = new ServiceCollection();
        services.AddMediatRR(_ => { }, new ConcurrentQueue<DeadLettersInfo>());
        services.AddNotificationHandler<Note, ThrowsTimeout>(new NotificationRetryPolicy { MaxRetryAttempts = 2, BackoffMultiplier = 2 });
        services.AddNotificationHandler<Note, ThrowsInvalidOperation>(new NotificationRetryPolicy { MaxRetryAttempts = 2, BackoffMultiplier = 3 });

        var provider = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<NotificationResiliencyProvider>());
    }
}
