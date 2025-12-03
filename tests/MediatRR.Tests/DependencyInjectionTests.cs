using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;

namespace MediatRR.Tests
{
    public class DependencyInjectionTests
    {

        [Fact]
        public void AddRequestHandler_ShouldRegisterHandler()
        {
            var services = new ServiceCollection();
            services.AddRequestHandler<TestRequest, string, TestRequestHandler>();

            var sp = services.BuildServiceProvider();
            var handler = sp.GetService<IRequestHandler<TestRequest, string>>();

            Assert.NotNull(handler);
            Assert.IsType<TestRequestHandler>(handler);
        }

        [Fact]
        public void AddMediatRR_ShouldApplyConfiguration()
        {
            var services = new ServiceCollection();
            var deadLetters = new ConcurrentQueue<DeadLettersInfo>();

            services.AddMediatRR(cfg =>
            {
                cfg.NotificationChannelSize = 500;
                cfg.MaxConcurrentMessageConsumer = 10;
            }, deadLetters);

            var sp = services.BuildServiceProvider();
            var config = sp.GetRequiredService<MediatRRConfiguration>();

            Assert.Equal(500, config.NotificationChannelSize);
            Assert.Equal(10, config.MaxConcurrentMessageConsumer);
        }

        [Fact]
        public void AddNotificationHandler_ShouldHandleConcurrentRegistrations()
        {
            var services = new ServiceCollection();
            var retryPolicy = new NotificationRetryPolicy();
            var deadLetters = new ConcurrentQueue<DeadLettersInfo>();

            services.AddMediatRR(null, deadLetters);

            // Register first handler to create dictionary
            services.AddNotificationHandler<TestNotification, TestNotificationHandler>(retryPolicy);


            var sp = services.BuildServiceProvider();
            sp.GetRequiredService<INotificationHandler<TestNotification>>();
            var policies = sp.GetRequiredService<NotificationResiliencyProvider>();
            Assert.Equal(retryPolicy, policies.GetResiliencyPolicy(typeof(TestNotification)));

        }
    }

    public class AnotherNotificationHandler : INotificationHandler<TestNotification>
    {
        public System.Threading.Tasks.Task Handle(TestNotification notification, System.Threading.CancellationToken cancellationToken)
        {
            return System.Threading.Tasks.Task.CompletedTask;
        }
    }

    public class TestNotificationHandler : INotificationHandler<TestNotification>
    {
        public Task Handle(TestNotification notification, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
