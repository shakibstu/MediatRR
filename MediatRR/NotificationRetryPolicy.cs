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
        /// Default is 3.
        /// </summary>
        public int MaxRetryAttempts { get; init; } = 3;

        /// <summary>
        /// Gets or initializes the delay between retry attempts.
        /// Default is 1 second.
        /// </summary>
        public TimeSpan DelayBetweenRetries { get; init; } = TimeSpan.FromSeconds(1);
    }
}
namespace System.Runtime.CompilerServices
{
}