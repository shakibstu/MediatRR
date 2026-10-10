namespace MediatRR.Contract.Messaging
{
    /// <summary>
    /// A notification whose handling must respect publish order for notifications sharing the same key.
    /// Notifications with the same <see cref="OrderingKey"/> are handled one after another; notifications with
    /// different keys, and notifications with a null key, are handled concurrently.
    /// </summary>
    public interface IOrderedNotification : INotification
    {
        /// <summary>
        /// Gets the key that groups notifications which must be handled in order. Null opts out of ordering.
        /// </summary>
        string OrderingKey { get; }
    }
}
