using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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

            var builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            var deadLetters = new ConcurrentQueue<DeadLettersInfo>();

            builder.Services.AddMediatRR(cfg => { }, deadLetters);

            // Register multiple handlers for the same notification
            builder.Services.AddNotificationHandler<OrderPlaced, SendEmailHandler>();
            builder.Services.AddNotificationHandler<OrderPlaced, UpdateInventoryHandler>();
            builder.Services.AddNotificationHandler<OrderPlaced, LogOrderHandler>();

            using var host = builder.Build();
            await host.StartAsync();
            var mediator = host.Services.GetRequiredService<IMediator>();

            // Publish the notification - all handlers will be called by the background worker
            await mediator.Publish(new OrderPlaced
            {
                OrderId = "ORD-12345",
                Amount = 99.99m
            });

            await host.StopAsync();
            Console.WriteLine("Host stopped: the queue was drained and all notification handlers executed.");
        }
    }
}
