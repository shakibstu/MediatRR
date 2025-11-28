
using System.Collections.Concurrent;

namespace MediatRR
{
    /// <summary>
    /// Internal container for the dead letter queue.
    /// Holds notifications that have failed after exceeding maximum retry attempts.
    /// </summary>
    internal sealed class InternalDeadLettersKeeper
    {
        /// <summary>
        /// Gets or initializes the queue containing failed notifications.
        /// </summary>
        public ConcurrentQueue<DeadLettersInfo> DeadLettersQueue { get; init; }
    }
}
