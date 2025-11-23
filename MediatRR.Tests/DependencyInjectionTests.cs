using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;

namespace MediatRR.Tests
{
    public class DependencyInjectionTests
    {
        [Fact]
        public void AddNotificationHandler_ShouldRegisterRetryPolicy()
        {
            var services = new ServiceCollection();
            var retryPolicy = new NotificationRetryPolicy { MaxRetryAttempts = 5 };

            services.AddNotificationHandler<TestNotification, TestNotificationHandler>(retryPolicy);

            var sp = services.BuildServiceProvider();
            var policies = sp.GetRequiredService<ConcurrentDictionary<Type, NotificationRetryPolicy>>();

            Assert.True(policies.ContainsKey(typeof(TestNotification)));
            Assert.Equal(5, policies[typeof(TestNotification)].MaxRetryAttempts);
        }

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
            
            // Register first handler to create dictionary
            services.AddNotificationHandler<TestNotification, TestNotificationHandler>(retryPolicy);
            
            // Register second handler for same notification type (should update dictionary safely)
            // Note: In real scenario, multiple handlers for same notification is valid, but dictionary key is Type.
            // The current implementation uses TryAdd, so it won't overwrite.
            // Let's verify it doesn't crash and dictionary persists.
            
            services.AddNotificationHandler<TestNotification, AnotherNotificationHandler>(retryPolicy);

            var sp = services.BuildServiceProvider();
            var policies = sp.GetRequiredService<ConcurrentDictionary<Type, NotificationRetryPolicy>>();

            Assert.Single(policies); // Key is Type, so only one entry per Notification Type
            Assert.Equal(retryPolicy, policies[typeof(TestNotification)]);
            
            var handlers = sp.GetServices<INotificationHandler<TestNotification>>();
            Assert.Equal(2, handlers.Count());
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
