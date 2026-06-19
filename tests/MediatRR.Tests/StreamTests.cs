using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace MediatRR.Tests
{
    public class StreamTests
    {
        private static readonly int[] Expected = [1, 2, 3];

        // Bug A regression: the DI scope must stay alive until enumeration completes, otherwise a
        // stream handler's scoped/IDisposable dependency is disposed before the stream is consumed.
        [Fact]
        public async Task CreateStream_ShouldNotDisposeScopedDependency_BeforeEnumerationCompletes()
        {
            var services = new ServiceCollection();
            services.AddMediatRR(cfg => { }, new ConcurrentQueue<DeadLettersInfo>());
            services.AddScoped<TrackedDependency>();
            services.AddStreamRequestHandler<TrackedStreamRequest, int, TrackedStreamHandler>();

            var sp = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            var mediator = sp.GetRequiredService<IMediator>();

            var items = new List<int>();
            await foreach (int item in mediator.CreateStream(new TrackedStreamRequest()))
            {
                items.Add(item);
            }

            Assert.Equal(Expected, items);
        }

        // Cancelling the token must stop enumeration (the token must reach the handler).
        [Fact]
        public async Task CreateStream_ShouldStopEnumeration_WhenCancelled()
        {
            var services = new ServiceCollection();
            services.AddMediatRR(cfg => { }, new ConcurrentQueue<DeadLettersInfo>());
            services.AddStreamRequestHandler<InfiniteStreamRequest, int, InfiniteStreamHandler>();

            var sp = services.BuildServiceProvider();
            var mediator = sp.GetRequiredService<IMediator>();

            using var cts = new CancellationTokenSource();
            var received = new List<int>();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await foreach (int item in mediator.CreateStream(new InfiniteStreamRequest(), cts.Token))
                {
                    received.Add(item);
                    if (received.Count == 3)
                    {
                        await cts.CancelAsync();
                    }
                }
            });

            Assert.Equal(3, received.Count);
        }

        // A stream behavior that throws must surface the exception to the consumer during enumeration.
        [Fact]
        public async Task CreateStream_ShouldPropagate_WhenStreamBehaviorThrows()
        {
            var services = new ServiceCollection();
            services.AddMediatRR(cfg => { }, new ConcurrentQueue<DeadLettersInfo>());
            services.AddTransient(typeof(IStreamBehavior<,>), typeof(ThrowingStreamBehavior<,>));
            services.AddStreamRequestHandler<InfiniteStreamRequest, int, InfiniteStreamHandler>();

            var sp = services.BuildServiceProvider();
            var mediator = sp.GetRequiredService<IMediator>();

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await foreach (int _ in mediator.CreateStream(new InfiniteStreamRequest())) { }
            });
        }

        public sealed class TrackedDependency : IDisposable
        {
            public bool Disposed { get; private set; }
            public void Dispose() => Disposed = true;

            public int Next() => Disposed
                ? throw new ObjectDisposedException(nameof(TrackedDependency))
                : 1;
        }

        public class TrackedStreamRequest : IStreamRequest<int> { }

        public class TrackedStreamHandler : IStreamRequestHandler<TrackedStreamRequest, int>
        {
            private readonly TrackedDependency _dep;
            public TrackedStreamHandler(TrackedDependency dep) => _dep = dep;

            public async IAsyncEnumerable<int> Handle(TrackedStreamRequest request,
                [EnumeratorCancellation] CancellationToken ct)
            {
                // Throws ObjectDisposedException if the scope was disposed before iteration (Bug A).
                yield return _dep.Next();
                yield return 2;
                yield return 3;
                await Task.CompletedTask;
            }
        }

        private class InfiniteStreamRequest : IStreamRequest<int> { }

        private class InfiniteStreamHandler : IStreamRequestHandler<InfiniteStreamRequest, int>
        {
            public async IAsyncEnumerable<int> Handle(InfiniteStreamRequest request,
                [EnumeratorCancellation] CancellationToken ct)
            {
                int i = 0;
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    yield return ++i;
                    await Task.Yield();
                }
            }
        }

        public class ThrowingStreamBehavior<TRequest, TResponse> : IStreamBehavior<TRequest, TResponse>
            where TRequest : notnull
        {
            public async IAsyncEnumerable<TResponse> Handle(TRequest request,
                Func<IAsyncEnumerable<TResponse>> next,
                [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                await Task.CompletedTask;
                throw new InvalidOperationException("stream behavior failed");
#pragma warning disable CS0162 // unreachable: required to make this method an async iterator
                yield break;
#pragma warning restore CS0162
            }
        }
    }
}
