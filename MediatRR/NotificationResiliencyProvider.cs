using System;
using System.Collections.Concurrent;

namespace MediatRR
{
    internal class NotificationResiliencyProvider
    {
        private readonly ConcurrentDictionary<Type, NotificationRetryPolicy> _resiliencies = new();
        
        public NotificationRetryPolicy GetResiliencyPolicy(Type notificationType)
        {
            return _resiliencies.GetOrAdd(notificationType, _ => new NotificationRetryPolicy
            {
                MaxRetryAttempts = 3,
                DelayBetweenRetries = TimeSpan.FromSeconds(2)
            });
        }
        public void SetResiliencyPolicy(Type notificationType, NotificationRetryPolicy policy)
        {
            _resiliencies[notificationType] = policy;
        }
    }
}
