using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;

namespace MediatRR.Documentation.Examples.Features
{
    // 1. Define the Notification
    public class OrderPlaced : INotification
    {
        public string OrderId { get; set; }
        public decimal Amount { get; set; }
    }

    // 2. Define Multiple Handlers
    public class SendEmailHandler : INotificationHandler<OrderPlaced>
    {
        public Task Handle(OrderPlaced notification, CancellationToken cancellationToken)
        {
            Console.WriteLine($"[Email] Sending confirmation for order {notification.OrderId}");
            return Task.CompletedTask;
        }
    }

    public class UpdateInventoryHandler : INotificationHandler<OrderPlaced>
    {
        public Task Handle(OrderPlaced notification, CancellationToken cancellationToken)
        {
            Console.WriteLine($"[Inventory] Updating stock for order {notification.OrderId}");
            return Task.CompletedTask;
        }
    }

    public class LogOrderHandler : INotificationHandler<OrderPlaced>
    {
        public Task Handle(OrderPlaced notification, CancellationToken cancellationToken)
        {
            Console.WriteLine($"[Log] Order placed: {notification.OrderId} - ${notification.Amount}");
            return Task.CompletedTask;
        }
    }

    // 3. Runner
    public static class NotificationRunner
    {
        public static async Task Run()
        {
            Console.WriteLine("\nRunning Notification Example...");

            var services = new ServiceCollection();
            var deadLetters = new ConcurrentQueue<DeadLettersInfo>();

            services.AddMediatRR(cfg => { }, deadLetters);

            // Register multiple handlers for the same notification
            services.AddNotificationHandler<OrderPlaced, SendEmailHandler>(null);
            services.AddNotificationHandler<OrderPlaced, UpdateInventoryHandler>(null);
            services.AddNotificationHandler<OrderPlaced, LogOrderHandler>(null);

            var provider = services.BuildServiceProvider();
            var mediator = provider.GetRequiredService<IMediator>();

            // Publish the notification - all handlers will be called
            await mediator.Publish(new OrderPlaced
            {
                OrderId = "ORD-12345",
                Amount = 99.99m
            });

            Console.WriteLine("All notification handlers executed!");
        }
    }
}
