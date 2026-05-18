using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace MediatRR
{
    internal sealed class NotificationResiliencyProvider
    {
        private readonly ConcurrentDictionary<Type, NotificationRetryPolicy> _resiliencies;

        public NotificationResiliencyProvider(IEnumerable<NotificationPolicyRegistration> registrations)
        {
            _resiliencies = new ConcurrentDictionary<Type, NotificationRetryPolicy>();
            foreach (var r in registrations)
            {
                SetResiliencyPolicy(r.NotificationType, r.Policy);
            }
        }

        public NotificationRetryPolicy GetResiliencyPolicy(Type notificationType) =>
            _resiliencies.TryGetValue(notificationType, out var p) ? p : NotificationRetryPolicy.Default;

        private void SetResiliencyPolicy(Type notificationType, NotificationRetryPolicy policy)
        {
            if (_resiliencies.TryAdd(notificationType, policy)) return;

            var existing = _resiliencies[notificationType];
            if (ReferenceEquals(existing, policy)) return;
            if (existing.MaxRetryAttempts == policy.MaxRetryAttempts &&
                existing.DelayBetweenRetries == policy.DelayBetweenRetries) return;

            throw new InvalidOperationException(
                $"Conflicting retry policies registered for {notificationType}. " +
                "Register one policy per notification type.");
        }
    }

    internal sealed class NotificationPolicyRegistration
    {
        public NotificationPolicyRegistration(Type notificationType, NotificationRetryPolicy policy)
        {
            NotificationType = notificationType;
            Policy = policy;
        }

        public Type NotificationType { get; }
        public NotificationRetryPolicy Policy { get; }
    }
}
