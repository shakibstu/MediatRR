using MediatRR.Contract.Messaging;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace MediatRR
{
    /// <summary>
    /// Internal channel for queuing and processing notifications asynchronously.
    /// </summary>
    internal sealed class NotificationChannel
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

        public async ValueTask<NotificationPublishContext> ReadFromChannel(CancellationToken cancellationToken)
        {
            return await _channel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }

        public bool TryRead(out NotificationPublishContext context)
        {
            return _channel.Reader.TryRead(out context);
        }

        /// <summary>
        /// Stops the channel from accepting new notifications.
        /// Idempotent — safe to call multiple times.
        /// </summary>
        public void Stop()
        {
            _channel.Writer.TryComplete();
        }

        public int Count => _channel.Reader.Count;

        /// <summary>
        /// Completes when the writer is completed and all items have been read.
        /// </summary>
        public Task Completion => _channel.Reader.Completion;
    }
}
