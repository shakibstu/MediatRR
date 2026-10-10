using System;

namespace MediatRR.Contract.Messaging
{
    /// <summary>
    /// A notification that failed after all retry attempts, handed to <see cref="IDeadLetterHandler{TNotification}"/>.
    /// </summary>
    /// <typeparam name="TNotification">The type of the failed notification</typeparam>
    public sealed class DeadLetter<TNotification>
        where TNotification : INotification
    {
        public DeadLetter(TNotification notification, Exception exception, int attemptCount, Type handlerType, DateTime lastAttemptedAt)
        {
            Notification = notification;
            Exception = exception;
            AttemptCount = attemptCount;
            HandlerType = handlerType;
            LastAttemptedAt = lastAttemptedAt;
        }

        /// <summary>
        /// Gets the notification that failed.
        /// </summary>
        public TNotification Notification { get; }

        /// <summary>
        /// Gets the exception thrown by the last attempt.
        /// </summary>
        public Exception Exception { get; }

        /// <summary>
        /// Gets the total number of attempts, including retries. Zero when no handler could be resolved.
        /// </summary>
        public int AttemptCount { get; }

        /// <summary>
        /// Gets the type of the notification handler that failed, or null when the failure happened before any handler ran.
        /// </summary>
        public Type HandlerType { get; }

        /// <summary>
        /// Gets the UTC time of the last attempt.
        /// </summary>
        public DateTime LastAttemptedAt { get; }
    }
}
