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

            await channel.AddToChannel(notification, CancellationToken.None);

            Assert.Equal(1, channel.Count);
        }

        [Fact(Timeout = 5000)]
        public async Task ReadFromChannel_ShouldRetrieveNotification()
        {
            var config = new MediatRRConfiguration { NotificationChannelSize = 10 };
            var channel = new NotificationChannel(config);
            var notification = new TestNotification { Message = "Test" };

            await channel.AddToChannel(notification, CancellationToken.None);

            Assert.Equal(1, channel.Count);

            var enumerator = await channel.ReadFromChannel(CancellationToken.None);
            Assert.Equal(notification, enumerator.Message);
        }
    }
}
