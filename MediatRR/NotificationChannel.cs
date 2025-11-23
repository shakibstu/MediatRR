using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MediatRR.Contract.Messaging;

namespace MediatRR
{

    internal class NotificationChannel
    {
        private readonly Channel<NotificationPublishContext> _channel;

        public NotificationChannel(MediatRRConfiguration configuration)
        {
            _channel = Channel.CreateBounded<NotificationPublishContext>(
                new BoundedChannelOptions(configuration.NotificationChannelSize)
                {
                    SingleReader = true,
                    SingleWriter = false
                });
        }

        public ValueTask AddToChannel<T>(T notification, CancellationToken cancellationToken) where T : INotification
        {
            return _channel.Writer.WriteAsync(new NotificationPublishContext(notification, notification.GetType()),
                cancellationToken);
        }

        public ValueTask AddToChannel(NotificationPublishContext notificationPublishContext,
            CancellationToken cancellationToken)
        {
            return _channel.Writer.WriteAsync(notificationPublishContext, cancellationToken);
        }

        public IAsyncEnumerable<NotificationPublishContext> ReadFromChannel(CancellationToken cancellationToken)
        {
            return _channel.Reader.ReadAllAsync(cancellationToken);
        }

        public void Stop()
        {
            _channel.Writer.Complete();
        }

        public int Count => _channel.Reader.Count;
    }
}