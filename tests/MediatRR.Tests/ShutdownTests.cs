using System.Collections.Concurrent;
using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MediatRR.Tests;

public class ShutdownTests
{
    public sealed class N : INotification { public int Id { get; set; } }

    public sealed class SlowHandler : INotificationHandler<N>
    {
        public static int Processed;
        public async Task Handle(N notification, CancellationToken ct)
        {
            await Task.Delay(200, ct);
            Interlocked.Increment(ref Processed);
        }
    }

    [Fact(Timeout = 10000)]
    public async Task StopAsync_drains_in_flight_messages()
    {
        var sc = new ServiceCollection();
        sc.AddMediatRR(_ => { }, new ConcurrentQueue<DeadLettersInfo>());
        sc.AddNotificationHandler<N, SlowHandler>();

        var sp = sc.BuildServiceProvider();
        var worker = sp.GetRequiredService<IHostedService>();
        await worker.StartAsync(default);
        var mediator = sp.GetRequiredService<IMediator>();

        SlowHandler.Processed = 0;
        for (var i = 0; i < 5; i++)
        {
            await mediator.Publish(new N { Id = i });
        }

        await worker.StopAsync(default);
        Assert.Equal(5, SlowHandler.Processed);
    }
}
