using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace MediatRR.Documentation.Examples.Features
{
    // 1. Define the Notification
    public class PaymentCaptured : INotification
    {
        public string PaymentId { get; set; } = string.Empty;
    }

    // 2. A handler that keeps failing
    public class SyncLedgerHandler : INotificationHandler<PaymentCaptured>
    {
        public Task Handle(PaymentCaptured notification, CancellationToken cancellationToken)
        {
            Console.WriteLine($"[Ledger] Syncing payment {notification.PaymentId}... failed");
            throw new InvalidOperationException("Ledger service unavailable");
        }
    }

    // 3. A typed dead letter handler for that notification
    public class PaymentCapturedDeadLetterHandler : IDeadLetterHandler<PaymentCaptured>
    {
        public Task Handle(DeadLetter<PaymentCaptured> deadLetter, CancellationToken cancellationToken)
        {
            Console.WriteLine($"[DeadLetter] Payment {deadLetter.Notification.PaymentId} failed in {deadLetter.HandlerType?.Name} after {deadLetter.AttemptCount} attempts: {deadLetter.Exception.Message}");
            return Task.CompletedTask;
        }
    }

    // 4. Runner
    public static class DeadLetterHandlerRunner
    {
        public static async Task Run()
        {
            Console.WriteLine("\nRunning Dead Letter Handler Example...");

            var builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            var deadLetters = new ConcurrentQueue<DeadLettersInfo>();

            builder.Services.AddMediatRR(cfg => { }, deadLetters);
            builder.Services.AddNotificationHandler<PaymentCaptured, SyncLedgerHandler>(new NotificationRetryPolicy { MaxRetryAttempts = 2 });
            builder.Services.AddDeadLetterHandler<PaymentCaptured, PaymentCapturedDeadLetterHandler>();

            using var host = builder.Build();
            await host.StartAsync();
            var mediator = host.Services.GetRequiredService<IMediator>();

            await mediator.Publish(new PaymentCaptured { PaymentId = "PAY-001" });

            await host.StopAsync();

            // The queue still has the entry, so existing monitoring keeps working
            Console.WriteLine($"Queue entries: {deadLetters.Count}");
        }
    }
}
