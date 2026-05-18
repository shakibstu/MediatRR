using System.Collections.Concurrent;
using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace MediatRR.Tests;

public class ScopeTests
{
    public sealed class Tracker { public int DisposedCount; }

    public sealed class ScopedThing : IDisposable
    {
        private readonly Tracker _tracker;
        public ScopedThing(Tracker tracker) { _tracker = tracker; }
        public bool Disposed { get; private set; }
        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            Interlocked.Increment(ref _tracker.DisposedCount);
        }
        public string Greet() => Disposed ? throw new ObjectDisposedException(nameof(ScopedThing)) : "alive";
    }

    public sealed class ScopedStreamReq : IStreamRequest<string> { }

    public sealed class ScopedStreamHandler : IStreamRequestHandler<ScopedStreamReq, string>
    {
        private readonly ScopedThing _thing;
        public ScopedStreamHandler(ScopedThing thing) { _thing = thing; }
        public async IAsyncEnumerable<string> Handle(ScopedStreamReq request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            for (var i = 0; i < 3; i++)
            {
                await Task.Yield();
                yield return _thing.Greet();
            }
        }
    }

    public sealed class ScopedReq : IRequest<string> { }
    public sealed class ScopedReqHandler : IRequestHandler<ScopedReq, string>
    {
        public static ScopedThing? HandlerSeen;
        private readonly ScopedThing _thing;
        public ScopedReqHandler(ScopedThing thing) { _thing = thing; }
        public Task<string> Handle(ScopedReq request, CancellationToken ct)
        {
            HandlerSeen = _thing;
            return Task.FromResult(_thing.Greet());
        }
    }

    public sealed class BehaviorThatSharesScope<TReq, TResp> : IPipelineBehavior<TReq, TResp>
    {
        public static ScopedThing? BehaviorSeen;
        private readonly ScopedThing _thing;
        public BehaviorThatSharesScope(ScopedThing thing) { _thing = thing; }
        public Task<TResp> Handle(TReq request, Func<Task<TResp>> next, CancellationToken ct)
        {
            BehaviorSeen = _thing;
            return next();
        }
    }

    [Fact]
    public async Task Stream_holds_scope_for_entire_iteration()
    {
        var tracker = new Tracker();
        var sc = new ServiceCollection();
        sc.AddSingleton(tracker);
        sc.AddScoped<ScopedThing>();
        sc.AddMediatRR(_ => { }, new ConcurrentQueue<DeadLettersInfo>());
        sc.AddStreamRequestHandler<ScopedStreamReq, string, ScopedStreamHandler>();

        var sp = sc.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var items = new List<string>();
        await foreach (var s in mediator.CreateStream(new ScopedStreamReq()))
            items.Add(s);

        Assert.Equal(new[] { "alive", "alive", "alive" }, items);
        Assert.Equal(1, tracker.DisposedCount);
    }

    [Fact]
    public async Task Send_shares_scope_between_pipeline_behavior_and_handler()
    {
        BehaviorThatSharesScope<ScopedReq, string>.BehaviorSeen = null;
        ScopedReqHandler.HandlerSeen = null;

        var sc = new ServiceCollection();
        sc.AddSingleton<Tracker>();
        sc.AddScoped<ScopedThing>();
        sc.AddMediatRR(_ => { }, new ConcurrentQueue<DeadLettersInfo>());
        sc.AddRequestHandler<ScopedReq, string, ScopedReqHandler>();
        sc.AddTransient(typeof(IPipelineBehavior<,>), typeof(BehaviorThatSharesScope<,>));

        var sp = sc.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();
        await mediator.Send(new ScopedReq());

        Assert.NotNull(BehaviorThatSharesScope<ScopedReq, string>.BehaviorSeen);
        Assert.NotNull(ScopedReqHandler.HandlerSeen);
        // Same scope -> same instance of the scoped service.
        Assert.Same(BehaviorThatSharesScope<ScopedReq, string>.BehaviorSeen, ScopedReqHandler.HandlerSeen);
    }
}
