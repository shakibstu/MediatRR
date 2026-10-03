using System.Collections.Concurrent;
using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MediatRR.Tests.Regression;

public class RegistrationTests
{
    public sealed class OrderPlaced : INotification { }

    public sealed class SendEmailHandler : INotificationHandler<OrderPlaced>
    {
        public Task Handle(OrderPlaced notification, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class UpdateInventoryHandler : INotificationHandler<OrderPlaced>
    {
        public Task Handle(OrderPlaced notification, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class AuditHandler : INotificationHandler<OrderPlaced>
    {
        public Task Handle(OrderPlaced notification, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public void Registering_twice_yields_a_single_worker()
    {
        var deadLetters = new ConcurrentQueue<DeadLettersInfo>();
        var services = new ServiceCollection();
        services.AddMediatRR(_ => { }, deadLetters);
        services.AddMediatRR(_ => { }, deadLetters);

        var provider = services.BuildServiceProvider();

        Assert.Single(provider.GetServices<IHostedService>());
    }

    [Fact]
    public void Zero_MaxConcurrentMessageConsumer_is_rejected()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            services.AddMediatRR(cfg => cfg.MaxConcurrentMessageConsumer = 0, new ConcurrentQueue<DeadLettersInfo>()));
    }

    [Fact]
    public void Zero_NotificationChannelSize_is_rejected()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            services.AddMediatRR(cfg => cfg.NotificationChannelSize = 0, new ConcurrentQueue<DeadLettersInfo>()));
    }

    [Fact]
    public void A_handler_without_a_policy_does_not_conflict_with_handlers_that_have_one()
    {
        var retryPolicy = new NotificationRetryPolicy { MaxRetryAttempts = 3, DelayBetweenRetries = TimeSpan.FromSeconds(1) };
        var services = new ServiceCollection();
        services.AddMediatRR(_ => { }, new ConcurrentQueue<DeadLettersInfo>());
        services.AddNotificationHandler<OrderPlaced, SendEmailHandler>(retryPolicy);
        services.AddNotificationHandler<OrderPlaced, UpdateInventoryHandler>(retryPolicy);
        services.AddNotificationHandler<OrderPlaced, AuditHandler>();

        var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IHostedService>());
        Assert.Equal(3, provider.GetRequiredService<NotificationResiliencyProvider>().GetResiliencyPolicy(typeof(OrderPlaced)).MaxRetryAttempts);
    }

    [Fact]
    public void A_negative_retry_policy_is_rejected()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            services.AddNotificationHandler<OrderPlaced, AuditHandler>(new NotificationRetryPolicy { MaxRetryAttempts = -1 }));
    }
}
