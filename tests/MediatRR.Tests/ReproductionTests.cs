using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MediatRR.Tests
{
    public class ReproductionTests
    {
        [Fact]
        public async Task ScopedService_ShouldThrow_WhenResolvedFromRootInWorker()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddMediatRR(new MediatRRConfiguration());
            services.AddSingleton(new InternalDeadLettersKeeper());

            // Register a scoped service
            services.AddScoped<IScopedService, ScopedService>();

            // Register a handler that depends on the scoped service
            services.AddNotificationHandler<TestNotification, ScopedHandler>(null);

            var sp = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            var mediator = sp.GetRequiredService<IMediator>();
            var worker = sp.GetRequiredService<IHostedService>();

            // Start the worker
            await worker.StartAsync(CancellationToken.None);

            // Act
            // Publish a notification
            await mediator.Publish(new TestNotification());

            // Allow some time for the worker to pick it up
            await Task.Delay(500);

            // Assert
            // We expect the handler to be executed if everything is working correctly.
            // Currently, this will fail because of the scope issue.
            Assert.True(ScopedHandler.Executed, "Handler should have executed successfully");

            await worker.StopAsync(CancellationToken.None);
        }

        public class TestNotification : INotification { }

        public interface IScopedService { }
        public class ScopedService : IScopedService { }

        public class ScopedHandler : INotificationHandler<TestNotification>
        {
            private readonly IScopedService _scopedService;
            public static bool Executed { get; set; } = false;

            public ScopedHandler(IScopedService scopedService)
            {
                _scopedService = scopedService;
            }

            public Task Handle(TestNotification notification, CancellationToken cancellationToken)
            {
                Executed = true;
                return Task.CompletedTask;
            }
        }
    }
}
