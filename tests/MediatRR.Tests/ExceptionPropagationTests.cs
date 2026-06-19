using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;

namespace MediatRR.Tests
{
    public class ExceptionPropagationTests
    {
        // An exception thrown inside a request pipeline behavior must surface to the Send caller.
        [Fact]
        public async Task Send_ShouldPropagate_WhenPipelineBehaviorThrows()
        {
            var services = new ServiceCollection();
            services.AddMediatRR(cfg => { }, new ConcurrentQueue<DeadLettersInfo>());
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ThrowingPipelineBehavior<,>));
            services.AddRequestHandler<PingRequest, string, PingHandler>();

            var sp = services.BuildServiceProvider();
            var mediator = sp.GetRequiredService<IMediator>();

            await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new PingRequest()));
        }

        // An exception thrown inside a request handler must surface to the Send caller.
        [Fact]
        public async Task Send_ShouldPropagate_WhenHandlerThrows()
        {
            var services = new ServiceCollection();
            services.AddMediatRR(cfg => { }, new ConcurrentQueue<DeadLettersInfo>());
            services.AddRequestHandler<ThrowingRequest, string, ThrowingRequestHandler>();

            var sp = services.BuildServiceProvider();
            var mediator = sp.GetRequiredService<IMediator>();

            await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new ThrowingRequest()));
        }

        // An exception thrown inside a notification behavior runs synchronously on the publish path
        // and must surface to the Publish caller.
        [Fact]
        public async Task Publish_ShouldPropagate_WhenNotificationBehaviorThrows()
        {
            var services = new ServiceCollection();
            services.AddMediatRR(cfg => { }, new ConcurrentQueue<DeadLettersInfo>());
            services.AddTransient(typeof(INotificationBehavior<>), typeof(ThrowingNotificationBehavior<>));

            var sp = services.BuildServiceProvider();
            var mediator = sp.GetRequiredService<IMediator>();

            await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Publish(new PingNotification()));
        }

        public class PingRequest : IRequest<string> { }

        public class PingHandler : IRequestHandler<PingRequest, string>
        {
            public Task<string> Handle(PingRequest request, CancellationToken cancellationToken) => Task.FromResult("pong");
        }

        public class ThrowingRequest : IRequest<string> { }

        public class ThrowingRequestHandler : IRequestHandler<ThrowingRequest, string>
        {
            public async Task<string> Handle(ThrowingRequest request, CancellationToken cancellationToken)
            {
                await Task.CompletedTask;
                throw new InvalidOperationException("handler failed");
            }
        }

        public class PingNotification : INotification { }

        public class ThrowingPipelineBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
            where TRequest : IRequest<TResponse>
        {
            public async Task<TResponse> Handle(TRequest request, Func<Task<TResponse>> next, CancellationToken cancellationToken)
            {
                await Task.CompletedTask;
                throw new InvalidOperationException("pipeline behavior failed");
            }
        }

        public class ThrowingNotificationBehavior<TNotification> : INotificationBehavior<TNotification>
            where TNotification : INotification
        {
            public async Task Handle(TNotification notification, Func<Task> next, CancellationToken cancellationToken)
            {
                await Task.CompletedTask;
                throw new InvalidOperationException("notification behavior failed");
            }
        }
    }
}
