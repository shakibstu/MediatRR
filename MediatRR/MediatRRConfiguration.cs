namespace MediatRR
{
    /// <summary>
    /// Configuration options for MediatRR.
    /// </summary>
    public sealed class MediatRRConfiguration
    {
        /// <summary>
        /// Gets or sets the maximum number of notifications that can be queued in the notification channel.
        /// Default is 10,000.
        /// </summary>
        public int NotificationChannelSize { get; set; }
        
        /// <summary>
        /// Gets or sets the maximum number of concurrent notification handlers that can execute simultaneously.
        /// Default is 5.
        /// </summary>
        public int MaxConcurrentMessageConsumer { get; set; }
    }
}
