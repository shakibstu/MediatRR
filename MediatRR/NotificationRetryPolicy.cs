using System;
using MediatRR.Contract.Messaging;

namespace MediatRR
{
    public class NotificationRetryPolicy
    {
        /// <summary>
        /// Maximum number of retry attempts
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 3;

        /// <summary>
        /// Delay between retry attempts
        /// </summary>
        public TimeSpan DelayBetweenRetries { get; set; } = TimeSpan.FromSeconds(1);
    }
}
