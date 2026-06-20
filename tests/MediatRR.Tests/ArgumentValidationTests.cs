using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;

namespace MediatRR.Tests
{
    public class ArgumentValidationTests
    {
        private static IMediator CreateMediator()
        {
            var services = new ServiceCollection();
            services.AddMediatRR(cfg => { }, new ConcurrentQueue<DeadLettersInfo>());
            return services.BuildServiceProvider().GetRequiredService<IMediator>();
        }

        [Fact]
        public async Task Send_ShouldThrowArgumentNullException_WhenRequestIsNull()
        {
            var mediator = CreateMediator();
            await Assert.ThrowsAsync<ArgumentNullException>(() => mediator.Send<string>(null!));
        }

        [Fact]
        public async Task Publish_ShouldThrowArgumentNullException_WhenNotificationIsNull()
        {
            var mediator = CreateMediator();
            await Assert.ThrowsAsync<ArgumentNullException>(() => mediator.Publish<INotification>(null!));
        }

        [Fact]
        public async Task CreateStream_ShouldThrowArgumentNullException_WhenRequestIsNull()
        {
            var mediator = CreateMediator();
            // CreateStream is an async iterator, so the null guard surfaces on first enumeration.
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            {
                await foreach (var _ in mediator.CreateStream<string>(null!)) { }
            });
        }
    }
}
