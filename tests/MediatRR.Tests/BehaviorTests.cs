using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;

namespace MediatRR.Tests
{
    public class BehaviorTests
    {
        [Fact]
        public async Task PipelineBehavior_ShouldRunInOrder()
        {
            var services = new ServiceCollection();
            var deadLetters = new ConcurrentQueue<DeadLettersInfo>();
            services.AddMediatRR(cfg => { }, deadLetters);

            services.AddSingleton<List<string>>();
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(BehaviorA<,>));
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(BehaviorB<,>));
            services.AddRequestHandler<TestRequest, string, BehaviorTestRequestHandler>();

            var sp = services.BuildServiceProvider();
            var mediator = sp.GetRequiredService<IMediator>();
            var log = sp.GetRequiredService<List<string>>();

            await mediator.Send(new TestRequest { Message = "Test" });

            Assert.Equal(new[] { "A Before", "B Before", "Handled", "B After", "A After" }, log);
        }

        [Fact]
        public async Task NotificationBehavior_ShouldRunInOrder()
        {
            var services = new ServiceCollection();
            var deadLetters = new ConcurrentQueue<DeadLettersInfo>();
            services.AddMediatRR(cfg => { }, deadLetters);

            services.AddSingleton<List<string>>();
            services.AddTransient(typeof(INotificationBehavior<>), typeof(NotificationBehaviorA<>));
            services.AddTransient(typeof(INotificationBehavior<>), typeof(NotificationBehaviorB<>));

            // We need to mock the internal publish mechanism or use a real one.
            // Since Publish uses ExecuteNotification internally, we can test it via Publish directly
            // but we need to be careful about the async nature if it goes to channel.
            // Wait, Mediator.Publish calls ExecuteNotification which runs behaviors AND THEN queues to channel.
            // So behaviors run synchronously before queuing!

            var sp = services.BuildServiceProvider();
            var mediator = sp.GetRequiredService<IMediator>();
            var log = sp.GetRequiredService<List<string>>();

            await mediator.Publish(new TestNotification { Message = "Event" });

            Assert.Equal(new[] { "NA Before", "NB Before", "NB After", "NA After" }, log);
        }

        [Fact]
        public async Task StreamBehavior_ShouldRunInOrder()
        {
            var services = new ServiceCollection();
            var deadLetters = new ConcurrentQueue<DeadLettersInfo>();
            services.AddMediatRR(cfg => { }, deadLetters);

            services.AddSingleton<List<string>>();
            services.AddTransient(typeof(IStreamBehavior<,>), typeof(StreamBehaviorA<,>));
            services.AddTransient(typeof(IStreamBehavior<,>), typeof(StreamBehaviorB<,>));
            services.AddStreamRequestHandler<TestStreamRequest, string, StreamBehaviorTestRequestHandler>();

            var sp = services.BuildServiceProvider();
            var mediator = sp.GetRequiredService<IMediator>();
            var log = sp.GetRequiredService<List<string>>();

            var stream = mediator.CreateStream(new TestStreamRequest { Message = "Stream" });
            var items = new List<string>();
            await foreach (var item in stream)
            {
                items.Add(item);
            }

            Assert.Equal(new[] { "SA Before", "SB Before", "SB After", "SA After" }, log);
            Assert.Equal(new[] { "Stream 1", "Stream 2" }, items);
        }

        public class BehaviorA<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
        {
            private readonly List<string> _log;
            public BehaviorA(List<string> log) => _log = log;

            public async Task<TResponse> Handle(TRequest request, Func<Task<TResponse>> next, CancellationToken cancellationToken)
            {
                _log.Add("A Before");
                var response = await next();
                _log.Add("A After");
                return response;
            }
        }

        public class BehaviorB<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
        {
            private readonly List<string> _log;
            public BehaviorB(List<string> log) => _log = log;

            public async Task<TResponse> Handle(TRequest request, Func<Task<TResponse>> next, CancellationToken cancellationToken)
            {
                _log.Add("B Before");
                var response = await next();
                _log.Add("B After");
                return response;
            }
        }

        public class NotificationBehaviorA<TNotification> : INotificationBehavior<TNotification> where TNotification : INotification
        {
            private readonly List<string> _log;
            public NotificationBehaviorA(List<string> log) => _log = log;

            public async Task Handle(TNotification notification, Func<Task> next, CancellationToken cancellationToken)
            {
                _log.Add("NA Before");
                await next();
                _log.Add("NA After");
            }
        }

        public class NotificationBehaviorB<TNotification> : INotificationBehavior<TNotification> where TNotification : INotification
        {
            private readonly List<string> _log;
            public NotificationBehaviorB(List<string> log) => _log = log;

            public async Task Handle(TNotification notification, Func<Task> next, CancellationToken cancellationToken)
            {
                _log.Add("NB Before");
                await next();
                _log.Add("NB After");
            }
        }

        public class StreamBehaviorA<TRequest, TResponse> : IStreamBehavior<TRequest, TResponse> where TRequest : notnull
        {
            private readonly List<string> _log;
            public StreamBehaviorA(List<string> log) => _log = log;

            public async IAsyncEnumerable<TResponse> Handle(TRequest request, Func<IAsyncEnumerable<TResponse>> next, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
            {
                _log.Add("SA Before");
                var stream = next();
                await foreach (var item in stream)
                {
                    yield return item;
                }
                _log.Add("SA After");
            }
        }

        public class StreamBehaviorB<TRequest, TResponse> : IStreamBehavior<TRequest, TResponse> where TRequest : notnull
        {
            private readonly List<string> _log;
            public StreamBehaviorB(List<string> log) => _log = log;

            public async IAsyncEnumerable<TResponse> Handle(TRequest request, Func<IAsyncEnumerable<TResponse>> next, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
            {
                _log.Add("SB Before");
                var stream = next();
                await foreach (var item in stream)
                {
                    yield return item;
                }
                _log.Add("SB After");
            }
        }

        // Reusing TestRequest/Handler from MediatorTests context if possible, but defining here for isolation or using shared if public.
        // Since they are in the same namespace, we can reuse if they are public.
        // Assuming TestRequest and TestRequestHandler are available from MediatorTests.cs (they were public).
        // But we need to inject List<string> into TestRequestHandler to log "Handled".
        // The existing TestRequestHandler doesn't use List<string>.
        // So we need a specific handler for this test.
        public class BehaviorTestRequestHandler : IRequestHandler<TestRequest, string>
        {
            private readonly List<string> _log;
            public BehaviorTestRequestHandler(List<string> log) => _log = log;

            public Task<string> Handle(TestRequest request, CancellationToken cancellationToken)
            {
                _log.Add("Handled");
                return Task.FromResult("Result");
            }
        }

        public class TestStreamRequest : IStreamRequest<string>
        {
            public string Message { get; set; }
        }

        public class StreamBehaviorTestRequestHandler : IStreamRequestHandler<TestStreamRequest, string>
        {
            private readonly List<string> _log;
            public StreamBehaviorTestRequestHandler(List<string> log) => _log = log;

            public async IAsyncEnumerable<string> Handle(TestStreamRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
            {
                yield return request.Message + " 1";
                yield return request.Message + " 2";
                await Task.CompletedTask;
            }
        }
    }
}
