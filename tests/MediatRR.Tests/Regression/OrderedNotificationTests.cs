using System.Collections.Concurrent;
using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace MediatRR.Tests.Regression;

public class OrderedNotificationTests
{
    public sealed class AccountEvent : IOrderedNotification
    {
        public string AccountId { get; set; } = string.Empty;
        public int Sequence { get; set; }
        public string OrderingKey => AccountId;
    }

    public sealed class RecordingHandler : INotificationHandler<AccountEvent>
    {
        public static readonly ConcurrentDictionary<string, List<int>> SeenByAccount = new();
        public static int Concurrent;
        public static int MaxConcurrent;

        public async Task Handle(AccountEvent notification, CancellationToken cancellationToken)
        {
            var now = Interlocked.Increment(ref Concurrent);
            InterlockedMax(ref MaxConcurrent, now);
            await Task.Delay(Random.Shared.Next(1, 8), cancellationToken);
            var seen = SeenByAccount.GetOrAdd(notification.AccountId ?? "<null>", _ => new List<int>());
            lock (seen) seen.Add(notification.Sequence);
            Interlocked.Decrement(ref Concurrent);
        }

        private static void InterlockedMax(ref int target, int value)
        {
            int current;
            while ((current = target) < value && Interlocked.CompareExchange(ref target, value, current) != current) { }
        }
    }

    public sealed class FlakyFirst : INotificationHandler<AccountEvent>
    {
        public static readonly ConcurrentQueue<int> Seen = new();
        public static int Failures;

        public Task Handle(AccountEvent notification, CancellationToken cancellationToken)
        {
            if (notification.Sequence == 1 && Interlocked.Increment(ref Failures) <= 2)
            {
                throw new InvalidOperationException("transient");
            }

            Seen.Enqueue(notification.Sequence);
            return Task.CompletedTask;
        }
    }

    public sealed class Unordered : INotification { public int Id { get; set; } }

    public sealed class SlowUnorderedHandler : INotificationHandler<Unordered>
    {
        public static int Concurrent;
        public static int MaxConcurrent;

        public async Task Handle(Unordered notification, CancellationToken cancellationToken)
        {
            var now = Interlocked.Increment(ref Concurrent);
            int current;
            while ((current = MaxConcurrent) < now && Interlocked.CompareExchange(ref MaxConcurrent, now, current) != current) { }
            await Task.Delay(30, cancellationToken);
            Interlocked.Decrement(ref Concurrent);
        }
    }

    [Fact]
    public async Task Notifications_with_the_same_key_are_handled_in_publish_order()
    {
        RecordingHandler.SeenByAccount.Clear();
        var services = new ServiceCollection();
        services.AddMediatRR(cfg => cfg.MaxConcurrentMessageConsumer = 8, new ConcurrentQueue<DeadLettersInfo>());
        services.AddNotificationHandler<AccountEvent, RecordingHandler>();
        var (_, worker, mediator) = await WorkerHost.StartAsync(services);

        for (var sequence = 1; sequence <= 20; sequence++)
        {
            await mediator.Publish(new AccountEvent { AccountId = "A", Sequence = sequence });
            await mediator.Publish(new AccountEvent { AccountId = "B", Sequence = sequence });
        }

        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(Enumerable.Range(1, 20), RecordingHandler.SeenByAccount["A"]);
        Assert.Equal(Enumerable.Range(1, 20), RecordingHandler.SeenByAccount["B"]);
    }

    [Fact]
    public async Task Different_keys_are_handled_concurrently()
    {
        RecordingHandler.SeenByAccount.Clear();
        RecordingHandler.Concurrent = 0;
        RecordingHandler.MaxConcurrent = 0;
        var services = new ServiceCollection();
        services.AddMediatRR(cfg => cfg.MaxConcurrentMessageConsumer = 8, new ConcurrentQueue<DeadLettersInfo>());
        services.AddNotificationHandler<AccountEvent, RecordingHandler>();
        var (_, worker, mediator) = await WorkerHost.StartAsync(services);

        for (var key = 0; key < 8; key++)
        {
            for (var sequence = 1; sequence <= 5; sequence++)
            {
                await mediator.Publish(new AccountEvent { AccountId = key.ToString(), Sequence = sequence });
            }
        }

        await worker.StopAsync(CancellationToken.None);

        Assert.True(RecordingHandler.MaxConcurrent > 1, $"max concurrency was {RecordingHandler.MaxConcurrent}");
    }

    [Fact]
    public async Task A_retried_notification_still_holds_its_key_so_later_ones_wait()
    {
        FlakyFirst.Seen.Clear();
        FlakyFirst.Failures = 0;
        var services = new ServiceCollection();
        services.AddMediatRR(cfg => cfg.MaxConcurrentMessageConsumer = 8, new ConcurrentQueue<DeadLettersInfo>());
        services.AddNotificationHandler<AccountEvent, FlakyFirst>(new NotificationRetryPolicy { MaxRetryAttempts = 3, DelayBetweenRetries = TimeSpan.FromMilliseconds(20) });
        var (_, worker, mediator) = await WorkerHost.StartAsync(services);

        for (var sequence = 1; sequence <= 3; sequence++)
        {
            await mediator.Publish(new AccountEvent { AccountId = "A", Sequence = sequence });
        }

        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(new[] { 1, 2, 3 }, FlakyFirst.Seen);
    }

    [Fact]
    public async Task Unordered_notifications_keep_running_concurrently()
    {
        SlowUnorderedHandler.Concurrent = 0;
        SlowUnorderedHandler.MaxConcurrent = 0;
        var services = new ServiceCollection();
        services.AddMediatRR(cfg => cfg.MaxConcurrentMessageConsumer = 4, new ConcurrentQueue<DeadLettersInfo>());
        services.AddNotificationHandler<Unordered, SlowUnorderedHandler>();
        var (_, worker, mediator) = await WorkerHost.StartAsync(services);

        for (var i = 0; i < 4; i++) await mediator.Publish(new Unordered { Id = i });
        await worker.StopAsync(CancellationToken.None);

        Assert.True(SlowUnorderedHandler.MaxConcurrent > 1, $"max concurrency was {SlowUnorderedHandler.MaxConcurrent}");
    }

    [Fact]
    public async Task An_ordered_notification_with_a_null_key_is_treated_as_unordered()
    {
        RecordingHandler.SeenByAccount.Clear();
        var services = new ServiceCollection();
        services.AddMediatRR(_ => { }, new ConcurrentQueue<DeadLettersInfo>());
        services.AddNotificationHandler<AccountEvent, RecordingHandler>();
        var (_, worker, mediator) = await WorkerHost.StartAsync(services);

        await mediator.Publish(new AccountEvent { AccountId = null!, Sequence = 1 });
        await worker.StopAsync(CancellationToken.None);

        Assert.Single(RecordingHandler.SeenByAccount);
    }
}
