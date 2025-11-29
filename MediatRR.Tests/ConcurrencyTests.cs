using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace MediatRR.Tests
{
    public class ConcurrencyTests
    {
        [Fact(Timeout = 10000)]
        public async Task Worker_ShouldRespectConcurrencyLimit()
        {
            // Arrange
            var services = new ServiceCollection();
            // Set concurrency to 2
            services.AddMediatRR(new MediatRRConfiguration { MaxConcurrentMessageConsumer = 2 });
            services.AddSingleton(new InternalDeadLettersKeeper());
            services.AddNotificationHandler<ConcurrentNotification, ConcurrentHandler>(null);

            var sp = services.BuildServiceProvider();
            var mediator = sp.GetRequiredService<IMediator>();
            var worker = sp.GetRequiredService<IHostedService>();

            await worker.StartAsync(CancellationToken.None);

            // Act
            // Publish 5 notifications. 
            // Since concurrency is 2, and handler takes 500ms, we expect:
            // T=0: 2 start
            // T=500: 2 finish, 2 start
            // T=1000: 2 finish, 1 starts
            // T=1500: 1 finishes
            
            ConcurrentHandler.ActiveHandlers = 0;
            ConcurrentHandler.MaxActiveHandlers = 0;

            var tasks = new Task[5];
            for (int i = 0; i < 5; i++)
            {
                tasks[i] = mediator.Publish(new ConcurrentNotification());
            }
            
            await Task.WhenAll(tasks);

            // Wait for all to complete
            while (ConcurrentHandler.ProcessedCount < 5)
            {
                await Task.Delay(100);
            }

            await worker.StopAsync(CancellationToken.None);

            // Assert
            Assert.True(ConcurrentHandler.MaxActiveHandlers <= 2, $"Max active handlers was {ConcurrentHandler.MaxActiveHandlers}, expected <= 2");
            Assert.Equal(5, ConcurrentHandler.ProcessedCount);
        }

        public class ConcurrentNotification : INotification { }

        public class ConcurrentHandler : INotificationHandler<ConcurrentNotification>
        {
            public static int ActiveHandlers = 0;
            public static int MaxActiveHandlers = 0;
            public static int ProcessedCount = 0;
            private static readonly object _lock = new();

            public async Task Handle(ConcurrentNotification notification, CancellationToken cancellationToken)
            {
                lock (_lock)
                {
                    ActiveHandlers++;
                    if (ActiveHandlers > MaxActiveHandlers)
                    {
                        MaxActiveHandlers = ActiveHandlers;
                    }
                }

                await Task.Delay(500, cancellationToken);

                lock (_lock)
                {
                    ActiveHandlers--;
                    ProcessedCount++;
                }
            }
        }
    }
}
