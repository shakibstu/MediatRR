using System;

namespace MediatRR
{
    /// <summary>
    /// Defines the retry policy for failed notification handlers.
    /// </summary>
    public sealed class NotificationRetryPolicy
    {
        /// <summary>
        /// Default policy: zero retries, no delay.
        /// </summary>
        public static NotificationRetryPolicy Default { get; } = new NotificationRetryPolicy();

        /// <summary>
        /// Gets or initializes the maximum number of retry attempts for a failed notification handler.
        /// </summary>
        public int MaxRetryAttempts { get; init; }

        /// <summary>
        /// Gets or initializes the delay before the first retry.
        /// </summary>
        public TimeSpan DelayBetweenRetries { get; init; } = TimeSpan.Zero;

        /// <summary>
        /// Gets or initializes the factor applied to the delay after each retry. 1 keeps the delay constant,
        /// 2 doubles it on every retry. Must be at least 1.
        /// </summary>
        public double BackoffMultiplier { get; init; } = 1;

        /// <summary>
        /// Gets or initializes the upper bound for a backed-off delay. Null means no bound.
        /// </summary>
        public TimeSpan? MaxDelayBetweenRetries { get; init; }

        /// <summary>
        /// Gets or initializes a predicate deciding whether a failure is worth retrying.
        /// Null retries every exception. When it returns false the notification is dead-lettered immediately.
        /// </summary>
        public Func<Exception, bool> ShouldRetry { get; init; }

        /// <summary>
        /// Returns the delay to wait before the retry that follows the given failed attempt (1-based).
        /// </summary>
        public TimeSpan GetRetryDelay(int failedAttempt)
        {
            if (failedAttempt < 1) throw new ArgumentOutOfRangeException(nameof(failedAttempt), failedAttempt, "Attempts are 1-based.");
            if (DelayBetweenRetries <= TimeSpan.Zero) return TimeSpan.Zero;

            var scaledTicks = DelayBetweenRetries.Ticks * Math.Pow(BackoffMultiplier, failedAttempt - 1);
            var delay = scaledTicks >= TimeSpan.MaxValue.Ticks ? TimeSpan.MaxValue : TimeSpan.FromTicks((long)scaledTicks);
            return MaxDelayBetweenRetries is { } max && delay > max ? max : delay;
        }

        internal bool IsRetryable(Exception exception) => ShouldRetry is null || ShouldRetry(exception);

        internal bool IsEquivalentTo(NotificationRetryPolicy other) =>
            MaxRetryAttempts == other.MaxRetryAttempts &&
            DelayBetweenRetries == other.DelayBetweenRetries &&
            BackoffMultiplier.Equals(other.BackoffMultiplier) &&
            MaxDelayBetweenRetries == other.MaxDelayBetweenRetries &&
            ReferenceEquals(ShouldRetry, other.ShouldRetry);
    }
}
