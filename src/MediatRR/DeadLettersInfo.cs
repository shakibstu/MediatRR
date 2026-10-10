using System;

namespace MediatRR
{
    /// <summary>
    /// Contains information about a notification that has been moved to the dead letter queue
    /// after exceeding the maximum retry attempts.
    /// </summary>
    public sealed class DeadLettersInfo
    {
        /// <param name="message">The notification message that failed</param>
        /// <param name="exception">The exception that caused the failure</param>
        /// <param name="attemptCount">The number of times the notification was attempted</param>
        /// <param name="lastAttemptedAt">The timestamp of the last attempt</param>
        public DeadLettersInfo(object message, Exception exception, int attemptCount, DateTime lastAttemptedAt)
            : this(message, exception, attemptCount, lastAttemptedAt, null)
        {
        }

        /// <param name="message">The notification message that failed</param>
        /// <param name="exception">The exception that caused the failure</param>
        /// <param name="attemptCount">The number of times the notification was attempted</param>
        /// <param name="lastAttemptedAt">The timestamp of the last attempt</param>
        /// <param name="handlerType">The handler that failed, or null when no handler ran</param>
        public DeadLettersInfo(object message, Exception exception, int attemptCount, DateTime lastAttemptedAt, Type handlerType)
        {
            Message = message;
            Exception = exception;
            AttemptCount = attemptCount;
            LastAttemptedAt = lastAttemptedAt;
            HandlerType = handlerType;
        }

        /// <summary>
        /// Gets the notification message that failed to be processed.
        /// </summary>
        public object Message { get; }

        /// <summary>
        /// Gets the exception that caused the notification processing to fail.
        /// </summary>
        public Exception Exception { get; }

        /// <summary>
        /// Gets the total number of attempts made to process this notification.
        /// </summary>
        public int AttemptCount { get; }

        /// <summary>
        /// Gets the timestamp when the last attempt was made.
        /// </summary>
        public DateTime LastAttemptedAt { get; }

        /// <summary>
        /// Gets the type of the handler that failed. Null when the failure happened before any handler ran,
        /// for example when handler resolution failed or the host stopped before the notification was read.
        /// For a failing <see cref="Contract.Messaging.IDeadLetterHandler{TNotification}"/> this is the dead letter handler's type.
        /// </summary>
        public Type HandlerType { get; }
    }
}
