using System;

namespace MediatRR
{
    /// <summary>
    /// Contains information about a notification that has been moved to the dead letter queue
    /// after exceeding the maximum retry attempts.
    /// </summary>
    /// <param name="message">The notification message that failed</param>
    /// <param name="exception">The exception that caused the failure</param>
    /// <param name="attemptCount">The number of times the notification was attempted</param>
    /// <param name="lastAttemptedAt">The timestamp of the last attempt</param>
    public sealed class DeadLettersInfo(object message, Exception exception, int attemptCount, DateTime lastAttemptedAt)
    {
        /// <summary>
        /// Gets the notification message that failed to be processed.
        /// </summary>
        public object Message { get; } = message;

        /// <summary>
        /// Gets the exception that caused the notification processing to fail.
        /// </summary>
        public Exception Exception { get; } = exception;

        /// <summary>
        /// Gets the total number of attempts made to process this notification.
        /// </summary>
        public int AttemptCount { get; } = attemptCount;

        /// <summary>
        /// Gets the timestamp when the last attempt was made.
        /// </summary>
        public DateTime LastAttemptedAt { get; } = lastAttemptedAt;
    }
}
