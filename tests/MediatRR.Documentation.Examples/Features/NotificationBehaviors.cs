using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace MediatRR.Documentation.Examples.Features
{
    // Notification Behavior - wraps the entire notification publishing process
    public class NotificationLoggingBehavior<TNotification> : INotificationBehavior<TNotification>
        where TNotification : INotification
    {
        public async Task Handle(TNotification notification, Func<Task> next, CancellationToken cancellationToken = default)
        {
            Console.WriteLine($"[NotificationBehavior] Before publishing {typeof(TNotification).Name}");
            await next();
            Console.WriteLine($"[NotificationBehavior] After publishing {typeof(TNotification).Name}");
        }
    }

    // Notification Handler Behavior - wraps each individual handler execution
    public class NotificationHandlerLoggingBehavior<TNotification> : INotificationHandlerBehavior<TNotification>
        where TNotification : INotification
    {
        public async Task Handle(TNotification notification, Func<Task> next, CancellationToken cancellationToken = default)
        {
            Console.WriteLine($"[HandlerBehavior] Before handler execution");
            await next();
            Console.WriteLine($"[HandlerBehavior] After handler execution");
        }
    }

    // Example notification and handler
    public class UserRegistered : INotification
    {
        public required string UserId { get; set; }
        public required string Email { get; set; }
    }

    public class WelcomeEmailHandler : INotificationHandler<UserRegistered>
    {
        public Task Handle(UserRegistered notification, CancellationToken cancellationToken)
        {
            Console.WriteLine($"[Handler] Sending welcome email to {notification.Email}");
            return Task.CompletedTask;
        }
    }
    public class SendGift : INotificationHandler<UserRegistered>
    {
        public Task Handle(UserRegistered notification, CancellationToken cancellationToken)
        {
            Console.WriteLine($"[Handler] Send Gift to {notification.Email}");
            return Task.CompletedTask;
        }
    }
    // Runner
    public static class NotificationBehaviorRunner
    {
        public static async Task Run()
        {
            Console.WriteLine("\nRunning Notification Behavior Example...");

            var builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            var deadLetters = new ConcurrentQueue<DeadLettersInfo>();

            builder.Services.AddMediatRR(cfg => { }, deadLetters);

            // Register behaviors
            builder.Services.AddTransient(typeof(INotificationBehavior<>), typeof(NotificationLoggingBehavior<>));
            builder.Services.AddTransient(typeof(INotificationHandlerBehavior<>), typeof(NotificationHandlerLoggingBehavior<>));

            // Register handler
            builder.Services.AddNotificationHandler<UserRegistered, WelcomeEmailHandler>();
            builder.Services.AddNotificationHandler<UserRegistered, SendGift>();

            using var host = builder.Build();
            await host.StartAsync();
            var mediator = host.Services.GetRequiredService<IMediator>();

            await mediator.Publish(new UserRegistered
            {
                UserId = "user-123",
                Email = "user@example.com"
            });

            await host.StopAsync();
            Console.WriteLine("Notification with behaviors completed!");
        }
    }
}
