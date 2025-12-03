using System;

namespace MediatRR
{
    /// <summary>
    /// Defines the retry policy for failed notification handlers.
    /// </summary>
    public class NotificationRetryPolicy
    {
        /// <summary>
        /// Gets or initializes the maximum number of retry attempts for a failed notification handler.
        /// </summary>
        public int MaxRetryAttempts { get; init; }

        /// <summary>
        /// Gets or initializes the delay between retry attempts.
        /// </summary>
        public TimeSpan DelayBetweenRetries { get; init; } = TimeSpan.Zero;
    }
}