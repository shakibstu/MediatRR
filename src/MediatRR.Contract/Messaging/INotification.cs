

namespace MediatRR.Contract.Messaging
{
    /// <summary>
    /// Marker interface to represent a notification message.
    /// Notifications are published to multiple handlers and do not return a response.
    /// </summary>
    public interface INotification { }
}
