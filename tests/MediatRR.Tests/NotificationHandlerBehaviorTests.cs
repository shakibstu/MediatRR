using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;

namespace MediatRR.Tests
{
    public class NotificationHandlerBehaviorTests
    {
        // Bug C regression + INotificationHandlerBehavior coverage: every handler for a notification must
        // see the behaviors in the same order. The in-place List.Reverse() inside the per-handler loop
        // toggles the order, so the second handler runs them reversed.
        [Fact(Timeout = 10000)]
        public async Task NotificationHandlerBehaviors_ShouldRunInConsistentOrder_AcrossHandlers()
        {
            var services = new ServiceCollection();
            services.AddMediatRR(cfg => { }, new ConcurrentQueue<DeadLettersInfo>());
            services.AddSingleton<ConcurrentQueue<string>>();
            services.AddTransient(typeof(INotificationHandlerBehavior<>), typeof(BehaviorOne<>));
            services.AddTransient(typeof(INotificationHandlerBehavior<>), typeof(BehaviorTwo<>));
            services.AddNotificationHandler<OrderNotification, HandlerA>(null);
            services.AddNotificationHandler<OrderNotification, HandlerB>(null);

            var sp = services.BuildServiceProvider();
            var mediator = sp.GetRequiredService<IMediator>();
            var worker = sp.GetRequiredService<IHostedService>();
            var log = sp.GetRequiredService<ConcurrentQueue<string>>();

            await worker.StartAsync(CancellationToken.None);
            await mediator.Publish(new OrderNotification());

            var retries = 0;
            while (log.Count < 4 && retries++ < 50)
            {
                await Task.Delay(100);
            }

            await worker.StopAsync(CancellationToken.None);

            // Each handler must see [B1, B2]; Bug C produces [B1, B2, B2, B1].
            Assert.Equal(new[] { "B1", "B2", "B1", "B2" }, log.ToArray());
        }

        public class OrderNotification : INotification { }

        public class HandlerA : INotificationHandler<OrderNotification>
        {
            public Task Handle(OrderNotification notification, CancellationToken cancellationToken) => Task.CompletedTask;
        }

        public class HandlerB : INotificationHandler<OrderNotification>
        {
            public Task Handle(OrderNotification notification, CancellationToken cancellationToken) => Task.CompletedTask;
        }

        public class BehaviorOne<T> : INotificationHandlerBehavior<T> where T : INotification
        {
            private readonly ConcurrentQueue<string> _log;
            public BehaviorOne(ConcurrentQueue<string> log) => _log = log;

            public Task Handle(T request, Func<Task> next, CancellationToken cancellationToken)
            {
                _log.Enqueue("B1");
                return next();
            }
        }

        public class BehaviorTwo<T> : INotificationHandlerBehavior<T> where T : INotification
        {
            private readonly ConcurrentQueue<string> _log;
            public BehaviorTwo(ConcurrentQueue<string> log) => _log = log;

            public Task Handle(T request, Func<Task> next, CancellationToken cancellationToken)
            {
                _log.Enqueue("B2");
                return next();
            }
        }
    }
}
