using System.Collections.Concurrent;
using System.Diagnostics;
using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MediatRR.Tests;

public class RetryConcurrencyTests
{
    public sealed class N : INotification { public int Id { get; set; } }

    public sealed class Failing : INotificationHandler<N>
    {
        public static int Attempts;
        public Task Handle(N notification, CancellationToken ct)
        {
            Interlocked.Increment(ref Attempts);
            throw new InvalidOperationException("boom");
        }
    }

    [Fact(Timeout = 15000)]
    public async Task Retry_delay_does_not_block_other_messages()
    {
        // 1 concurrent slot, 1s retry delay, 1 retry. With the old code a failing message
        // held the semaphore through the 1s delay so a second message had to wait — total ~2s.
        // With the fix the semaphore is released during the delay so retries overlap (~1s).
        var dlq = new ConcurrentQueue<DeadLettersInfo>();
        var sc = new ServiceCollection();
        sc.AddMediatRR(cfg => cfg.MaxConcurrentMessageConsumer = 1, dlq);
        sc.AddNotificationHandler<N, Failing>(new NotificationRetryPolicy
        {
            MaxRetryAttempts = 1,
            DelayBetweenRetries = TimeSpan.FromSeconds(1)
        });

        var sp = sc.BuildServiceProvider();
        var worker = sp.GetRequiredService<IHostedService>();
        await worker.StartAsync(default);

        Failing.Attempts = 0;
        var mediator = sp.GetRequiredService<IMediator>();

        var sw = Stopwatch.StartNew();
        await mediator.Publish(new N { Id = 1 });
        await mediator.Publish(new N { Id = 2 });

        while (dlq.Count < 2 && sw.ElapsedMilliseconds < 12000)
        {
            await Task.Delay(25);
        }
        sw.Stop();

        await worker.StopAsync(default);

        Assert.Equal(2, dlq.Count);
        // Under the bug we'd take ~2s; with the fix the two retry delays overlap to ~1s.
        Assert.True(sw.Elapsed < TimeSpan.FromMilliseconds(1800),
            $"Expected ~1s overlap, took {sw.Elapsed}");
    }
}
