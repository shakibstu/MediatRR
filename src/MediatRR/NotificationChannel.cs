using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace MediatRR;

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

    public ValueTask AddToChannel(NotificationPublishContext notificationPublishContext, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(notificationPublishContext, cancellationToken);

    public ValueTask<NotificationPublishContext> ReadFromChannel(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAsync(cancellationToken);

    public bool TryRead(out NotificationPublishContext context) => _channel.Reader.TryRead(out context);

    /// <summary>
    /// Stops the channel from accepting new notifications.
    /// Idempotent — safe to call multiple times.
    /// </summary>
    public void Stop() => _channel.Writer.TryComplete();
}
