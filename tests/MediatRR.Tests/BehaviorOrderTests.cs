using System.Collections.Concurrent;
using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MediatRR.Tests;

public class BehaviorOrderTests
{
    public sealed class N : INotification { }

    public sealed class H1 : INotificationHandler<N>
    {
        public Task Handle(N notification, CancellationToken ct) { lock (Log) Log.Add("H1"); return Task.CompletedTask; }
    }
    public sealed class H2 : INotificationHandler<N>
    {
        public Task Handle(N notification, CancellationToken ct) { lock (Log) Log.Add("H2"); return Task.CompletedTask; }
    }

    public sealed class B1 : INotificationHandlerBehavior<N>
    {
        public async Task Handle(N r, Func<Task> next, CancellationToken ct = default)
        {
            lock (Log) Log.Add("B1>");
            await next();
            lock (Log) Log.Add("<B1");
        }
    }

    public sealed class B2 : INotificationHandlerBehavior<N>
    {
        public async Task Handle(N r, Func<Task> next, CancellationToken ct = default)
        {
            lock (Log) Log.Add("B2>");
            await next();
            lock (Log) Log.Add("<B2");
        }
    }

    public static readonly List<string> Log = new();

    [Fact]
    public async Task Two_handlers_get_behaviors_in_same_order()
    {
        Log.Clear();
        var sc = new ServiceCollection();
        sc.AddMediatRR(_ => { }, new ConcurrentQueue<DeadLettersInfo>());
        sc.AddNotificationHandler<N, H1>();
        sc.AddNotificationHandler<N, H2>();
        sc.AddTransient<INotificationHandlerBehavior<N>, B1>();
        sc.AddTransient<INotificationHandlerBehavior<N>, B2>();

        var sp = sc.BuildServiceProvider();
        var worker = sp.GetRequiredService<IHostedService>();
        await worker.StartAsync(default);

        await sp.GetRequiredService<IMediator>().Publish(new N());

        await worker.StopAsync(default);

        // For both handlers, behaviors must run as B1 -> B2 -> handler -> <B2 -> <B1.
        Assert.Equal(2, Log.Count(s => s == "B1>"));
        Assert.Equal(2, Log.Count(s => s == "B2>"));
        Assert.Equal(2, Log.Count(s => s == "<B1"));
        Assert.Equal(2, Log.Count(s => s == "<B2"));

        // The first B1> must come before the first B2> for each handler.
        // Find index of every "B1>" / "B2>" entry; each B1> must have a later B2> before any handler runs.
        var first = Log.IndexOf("B1>");
        var second = Log.IndexOf("B2>");
        Assert.True(first < second);
    }
}
