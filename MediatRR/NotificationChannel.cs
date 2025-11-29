using MediatRR.Contract.Messaging;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace MediatRR
{
    /// <summary>
    /// Internal channel for queuing and processing notifications asynchronously.
    /// Uses a bounded channel to manage notification flow.
    /// </summary>
    internal sealed class NotificationChannel
    {
        private readonly Channel<NotificationPublishContext> _channel;

        /// <summary>
        /// Initializes a new instance of the <see cref="NotificationChannel"/> class.
        /// </summary>
        /// <param name="configuration">Configuration containing channel size and concurrency settings</param>
        public NotificationChannel(MediatRRConfiguration configuration)
        {
            _channel = Channel.CreateBounded<NotificationPublishContext>(
                new BoundedChannelOptions(configuration.NotificationChannelSize)
                {
                    SingleReader = true,
                    SingleWriter = false
                });
        }

        /// <summary>
        /// Adds a notification to the channel for asynchronous processing.
        /// </summary>
        /// <typeparam name="T">The type of notification</typeparam>
        /// <param name="notification">The notification to add</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>A task representing the asynchronous operation</returns>
        public ValueTask AddToChannel<T>(T notification, CancellationToken cancellationToken) where T : INotification
        {
            return _channel.Writer.WriteAsync(new NotificationPublishContext(notification, notification.GetType()),
                cancellationToken);
        }

        /// <summary>
        /// Adds a notification context to the channel for retry processing.
        /// </summary>
        /// <param name="notificationPublishContext">The notification context to add</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>A task representing the asynchronous operation</returns>
        public ValueTask AddToChannel(NotificationPublishContext notificationPublishContext,
            CancellationToken cancellationToken)
        {
            return _channel.Writer.WriteAsync(notificationPublishContext, cancellationToken);
        }

        /// <summary>
        /// Reads all notifications from the channel asynchronously.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>An async enumerable of notification contexts</returns>
        public async ValueTask<NotificationPublishContext> ReadFromChannel(CancellationToken cancellationToken)
        {
            return await _channel.Reader.ReadAsync(cancellationToken);
        }

        /// <summary>
        /// Stops the channel from accepting new notifications.
        /// Existing notifications in the channel will still be processed.
        /// </summary>
        public void Stop()
        {
            _channel.Writer.Complete();
        }

        /// <summary>
        /// Gets the current number of notifications waiting in the channel.
        /// </summary>
        public int Count => _channel.Reader.Count;
    }
}