using System.Threading;
using System.Threading.Tasks;

namespace MediatRR.Contract.Messaging
{
    /// <summary>
    /// Handles a notification of type <typeparamref name="TNotification"/> after it has been dead-lettered.
    /// Runs in the same scope as the handler that failed. It is never retried; if it throws, the exception is
    /// recorded in the dead letter queue.
    /// </summary>
    /// <typeparam name="TNotification">The type of notification being handled</typeparam>
    public interface IDeadLetterHandler<TNotification>
        where TNotification : INotification
    {
        /// <summary>
        /// Handles a dead-lettered notification.
        /// </summary>
        /// <param name="deadLetter">The failed notification together with the failure details</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task Handle(DeadLetter<TNotification> deadLetter, CancellationToken cancellationToken);
    }
}
