namespace MediatRR.Tests
{
    public class NotificationChannelTests
    {
        [Fact]
        public async Task AddToChannel_ShouldAddNotification()
        {
            var config = new MediatRRConfiguration { NotificationChannelSize = 10 };
            var channel = new NotificationChannel(config);
            var notification = new TestNotification { Message = "Test" };

            await channel.AddToChannel(new NotificationPublishContext(notification, notification.GetType()), CancellationToken.None);

            Assert.True(channel.TryRead(out var context));
            Assert.Same(notification, context.Message);
        }

        [Fact(Timeout = 5000)]
        public async Task ReadFromChannel_ShouldRetrieveNotification()
        {
            var config = new MediatRRConfiguration { NotificationChannelSize = 10 };
            var channel = new NotificationChannel(config);
            var notification = new TestNotification { Message = "Test" };

            await channel.AddToChannel(new NotificationPublishContext(notification, notification.GetType()), CancellationToken.None);

            var context = await channel.ReadFromChannel(CancellationToken.None);
            Assert.Equal(notification, context.Message);
        }
    }
}
