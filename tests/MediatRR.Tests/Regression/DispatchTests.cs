using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace MediatRR.Tests.Regression;

public class DispatchTests
{
    public sealed class Ping : IRequest<string> { }

    public sealed class ExplicitPingHandler : IRequestHandler<Ping, string>
    {
        Task<string> IRequestHandler<Ping, string>.Handle(Ping request, CancellationToken cancellationToken) => Task.FromResult("pong");
    }

    public sealed class ReqA : IRequest<int> { }

    public sealed class ReqB : IRequest<int> { }

    public sealed class MultiHandler : IRequestHandler<ReqA, int>, IRequestHandler<ReqB, int>
    {
        public Task<int> Handle(ReqA request, CancellationToken cancellationToken) => Task.FromResult(1);

        public Task<int> Handle(ReqB request, CancellationToken cancellationToken) => Task.FromResult(2);
    }

    public sealed class Boom : IRequest<string> { }

    public sealed class BoomHandler : IRequestHandler<Boom, string>
    {
        public Task<string> Handle(Boom request, CancellationToken cancellationToken) => throw new InvalidOperationException("boom");
    }

    public sealed class AsyncOnlyDisposable : IAsyncDisposable
    {
        public static int Disposed;

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref Disposed);
            return default;
        }
    }

    public sealed class ReqAsyncDep : IRequest<string> { }

    public sealed class ReqAsyncDepHandler(AsyncOnlyDisposable dependency) : IRequestHandler<ReqAsyncDep, string>
    {
        public Task<string> Handle(ReqAsyncDep request, CancellationToken cancellationToken) => Task.FromResult(dependency is null ? "null" : "ok");
    }

    public sealed class StreamReq : IStreamRequest<int> { }

    public sealed class ExplicitStreamHandler : IStreamRequestHandler<StreamReq, int>
    {
        async IAsyncEnumerable<int> IStreamRequestHandler<StreamReq, int>.Handle(StreamReq request, [EnumeratorCancellation] CancellationToken ct)
        {
            yield return 1;
            yield return 2;
            await Task.CompletedTask;
        }
    }

    public sealed class CountingNotification : INotification { }

    public sealed class CountingCtorHandler : INotificationHandler<CountingNotification>
    {
        public static int Constructed;

        public CountingCtorHandler()
        {
            Interlocked.Increment(ref Constructed);
        }

        public Task Handle(CountingNotification notification, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static ServiceCollection NewServices()
    {
        var services = new ServiceCollection();
        services.AddMediatRR(_ => { }, new ConcurrentQueue<DeadLettersInfo>());
        return services;
    }

    [Fact]
    public async Task Send_dispatches_to_an_explicit_interface_implementation()
    {
        var services = NewServices();
        services.AddRequestHandler<Ping, string, ExplicitPingHandler>();
        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();

        Assert.Equal("pong", await mediator.Send(new Ping()));
    }

    [Fact]
    public async Task Send_dispatches_to_a_class_implementing_two_handler_interfaces()
    {
        var services = NewServices();
        services.AddRequestHandler<ReqA, int, MultiHandler>();
        services.AddRequestHandler<ReqB, int, MultiHandler>();
        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();

        Assert.Equal(1, await mediator.Send(new ReqA()));
        Assert.Equal(2, await mediator.Send(new ReqB()));
    }

    [Fact]
    public async Task Send_preserves_the_type_of_a_synchronously_thrown_exception()
    {
        var services = NewServices();
        services.AddRequestHandler<Boom, string, BoomHandler>();
        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new Boom()));
        Assert.Equal("boom", ex.Message);
    }

    [Fact]
    public async Task Send_disposes_an_async_only_scoped_dependency_without_throwing()
    {
        var services = NewServices();
        services.AddScoped<AsyncOnlyDisposable>();
        services.AddRequestHandler<ReqAsyncDep, string, ReqAsyncDepHandler>();
        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();
        var before = AsyncOnlyDisposable.Disposed;

        Assert.Equal("ok", await mediator.Send(new ReqAsyncDep()));
        Assert.Equal(before + 1, AsyncOnlyDisposable.Disposed);
    }

    [Fact]
    public async Task CreateStream_dispatches_to_an_explicit_interface_implementation()
    {
        var services = NewServices();
        services.AddStreamRequestHandler<StreamReq, int, ExplicitStreamHandler>();
        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();

        var items = new List<int>();
        await foreach (var item in mediator.CreateStream(new StreamReq()))
        {
            items.Add(item);
        }

        Assert.Equal([1, 2], items);
    }

    [Fact]
    public async Task Publish_does_not_construct_a_handler_to_check_for_registrations()
    {
        var services = NewServices();
        services.AddNotificationHandler<CountingNotification, CountingCtorHandler>();
        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();
        var before = CountingCtorHandler.Constructed;

        await mediator.Publish(new CountingNotification());

        Assert.Equal(before, CountingCtorHandler.Constructed);
    }
}
