using System.Collections.Concurrent;
using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace MediatRR.Tests;

public class RetryPolicyTests
{
    public sealed class N : INotification { }
    public sealed class H1 : INotificationHandler<N> { public Task Handle(N _, CancellationToken __) => Task.CompletedTask; }
    public sealed class H2 : INotificationHandler<N> { public Task Handle(N _, CancellationToken __) => Task.CompletedTask; }

    [Fact]
    public void Conflicting_policies_for_same_notification_throw_at_resolve_time()
    {
        var sc = new ServiceCollection();
        sc.AddMediatRR(_ => { }, new ConcurrentQueue<DeadLettersInfo>());
        sc.AddNotificationHandler<N, H1>(new NotificationRetryPolicy { MaxRetryAttempts = 3 });
        sc.AddNotificationHandler<N, H2>(new NotificationRetryPolicy { MaxRetryAttempts = 7 });

        var sp = sc.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<NotificationResiliencyProvider>());
    }

    [Fact]
    public void Equivalent_policies_registered_twice_are_fine()
    {
        var sc = new ServiceCollection();
        sc.AddMediatRR(_ => { }, new ConcurrentQueue<DeadLettersInfo>());
        sc.AddNotificationHandler<N, H1>(new NotificationRetryPolicy { MaxRetryAttempts = 5 });
        sc.AddNotificationHandler<N, H2>(new NotificationRetryPolicy { MaxRetryAttempts = 5 });

        var sp = sc.BuildServiceProvider();
        var provider = sp.GetRequiredService<NotificationResiliencyProvider>();
        Assert.Equal(5, provider.GetResiliencyPolicy(typeof(N)).MaxRetryAttempts);
    }
}
