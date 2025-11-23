using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;

namespace MediatRR.Tests
{
    public class WorkerTests
    {
        [Fact(Timeout = 10000)]
        public async Task Worker_ShouldProcessNotification()
        {
            var services = new ServiceCollection();
            var deadLetters = new ConcurrentQueue<DeadLettersInfo>();
            services.AddMediatRR(cfg => { }, deadLetters);
            services.AddNotificationHandler<SuccessNotification, SuccessHandler>(null);

            var sp = services.BuildServiceProvider();
            var mediator = sp.GetRequiredService<IMediator>();
            var worker = sp.GetRequiredService<IHostedService>();

            await worker.StartAsync(CancellationToken.None);

            var notification = new SuccessNotification { Message = "Work" };
            await mediator.Publish(notification);

            // Wait for processing
            int retries = 0;
            while (!SuccessHandler.Processed && retries < 20)
            {
                await Task.Delay(100);
                retries++;
            }

            await worker.StopAsync(CancellationToken.None);

            Assert.True(SuccessHandler.Processed);
        }

        [Fact(Timeout = 10000)]
        public async Task Worker_ShouldRetryAndMoveToDeadLetter_WhenHandlerFails()
        {
            var services = new ServiceCollection();
            var deadLetters = new ConcurrentQueue<DeadLettersInfo>();
            services.AddMediatRR(cfg => { }, deadLetters);

            // Retry 2 times (total 3 attempts), fast delay
            var retryPolicy = new NotificationRetryPolicy
            {
                MaxRetryAttempts = 2,
                DelayBetweenRetries = TimeSpan.FromMilliseconds(50)
            };

            services.AddNotificationHandler<FailNotification, FailHandler>(retryPolicy);

            var sp = services.BuildServiceProvider();
            var mediator = sp.GetRequiredService<IMediator>();
            var worker = sp.GetRequiredService<IHostedService>();
            var deadLetterQueue = sp.GetRequiredService<InternalDeadLettersKeeper>().DeadLettersQueue;

            await worker.StartAsync(CancellationToken.None);

            var notification = new FailNotification();
            await mediator.Publish(notification);

            // Wait for dead letter
            int retries = 0;
            while (deadLetterQueue.IsEmpty && retries < 50)
            {
                await Task.Delay(100);
                retries++;
            }

            await worker.StopAsync(CancellationToken.None);

            Assert.False(deadLetterQueue.IsEmpty);
            Assert.True(deadLetterQueue.TryPeek(out var info));
            Assert.Equal(2, info.AttemptCount); // Should match MaxRetryAttempts
            Assert.IsType<FailNotification>(info.Message);
        }

        [Fact(Timeout = 10000)]
        public async Task Worker_StopAsync_ShouldWaitForChannelToEmpty()
        {
            var services = new ServiceCollection();
            var deadLetters = new ConcurrentQueue<DeadLettersInfo>();
            services.AddMediatRR(cfg => { }, deadLetters);
            services.AddNotificationHandler<SlowNotification, SlowHandler>(null);

            var sp = services.BuildServiceProvider();
            var mediator = sp.GetRequiredService<IMediator>();
            var worker = sp.GetRequiredService<IHostedService>();
            var cancellationToken = CancellationToken.None;
            await worker.StartAsync(cancellationToken);

            // Publish multiple messages and Stop immediately
            await await Task.WhenAll(mediator.Publish(new SlowNotification()), mediator.Publish(new SlowNotification()))
                .ContinueWith(_ =>
                {
                    cancellationToken = new CancellationToken(true);
                    return worker.StopAsync(CancellationToken.None);
                });

            // Verify handlers are processed
            Assert.Equal(2, SlowHandler.ProcessedCount);
        }
    }

    public class SlowNotification : INotification { }

    public class SlowHandler : INotificationHandler<SlowNotification>
    {
        public static int ProcessedCount = 0;
        public async Task Handle(SlowNotification notification, CancellationToken cancellationToken)
        {
            await Task.Delay(100); // Simulate work
            Interlocked.Increment(ref ProcessedCount);
        }
    }

    public class SuccessNotification : INotification
    {
        public string Message { get; set; }
    }

    public class SuccessHandler : INotificationHandler<SuccessNotification>
    {
        public static bool Processed = false;

        public Task Handle(SuccessNotification notification, CancellationToken cancellationToken)
        {
            Processed = true;
            return Task.CompletedTask;
        }
    }

    public class FailNotification : INotification { }

    public class FailHandler : INotificationHandler<FailNotification>
    {
        public Task Handle(FailNotification notification, CancellationToken cancellationToken)
        {
            throw new Exception("Failed");
        }
    }
}
