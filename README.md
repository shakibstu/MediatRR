# MediatRR

MediatRR is a powerful mediator pattern implementation for .NET applications. It helps you decouple your application logic by providing a simple, elegant way to send requests and publish notifications.

## Key Features

- **Request/Response Pattern**: Send requests and get typed responses
- **Streams**: Support for IAsyncEnumerable streams
- **Notifications**: Publish events to multiple handlers
- **Pipeline Behaviors**: Add cross-cutting concerns like logging, validation, and caching
- **Background Workers**: Process notifications asynchronously
- **Resilience**: Built-in retry policies and dead-letter queues
- **Dependency Injection**: First-class support for Microsoft.Extensions.DependencyInjection

## Installation

Install MediatRR via NuGet Package Manager or the .NET CLI:

```bash
dotnet add package MediatRR
```

## Basic Setup

Register MediatRR in your dependency injection container. The library requires a configuration action and a non-null dead-letter queue for handling failed notifications.

```csharp
using MediatRR;
using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;

var services = new ServiceCollection();
var deadLetters = new ConcurrentQueue<DeadLettersInfo>();

// Register MediatRR
services.AddMediatRR(cfg => 
{
    cfg.NotificationChannelSize = 100;
    cfg.MaxConcurrentMessageConsumer = 5;
}, deadLetters);

var provider = services.BuildServiceProvider();
var mediator = provider.GetRequiredService<IMediator>();
```

`IMediator` is registered as transient. `Send` and `CreateStream` each open their own DI scope, shared between the pipeline behaviors and the handler, so scoped dependencies such as a `DbContext` resolve once per call. For `CreateStream` that scope lives until the returned `IAsyncEnumerable` is fully enumerated or its enumerator is disposed. Handlers never share the scope of the code that calls the mediator: a scoped service your controller holds is a different instance from the one the handler receives. `Publish` opens a scope for its notification behaviors and then queues the notification; each handler runs later in the background worker, in a per-message scope that stays alive until every handler of that message has finished.

### Notifications need a running host

Notification handlers are executed by a hosted background service. In ASP.NET Core or the generic host it starts with the application. If you build a plain `ServiceCollection`, nothing starts it: `Publish` queues the notification and no handler runs. Either use `Host.CreateApplicationBuilder` (see the notification examples) or start the worker yourself:

```csharp
var worker = provider.GetRequiredService<IHostedService>();
await worker.StartAsync(CancellationToken.None);
// ... publish ...
await worker.StopAsync(CancellationToken.None); // drains the queue before returning
```

### Configuration Options

The `AddMediatRR` method accepts a configuration action with the following options:

- `NotificationChannelSize`: The size of the notification channel buffer (default: 10,000)
- `MaxConcurrentMessageConsumer`: Maximum concurrent notification handlers (default: 5)

Both values must be at least 1; `AddMediatRR` throws `ArgumentOutOfRangeException` otherwise. `MaxConcurrentMessageConsumer` caps how many handler executions run at once. When every slot is busy and the channel is full, `Publish` waits for room instead of growing memory.

### Dead Letter Queue

The `deadLetters` parameter is a `ConcurrentQueue<DeadLettersInfo>` that collects notifications that failed to process after all retry attempts. This allows you to:

- Monitor and log failed notifications
- Implement custom retry logic or manual intervention
- Analyze patterns in notification failures
- Ensure no notifications are silently lost

Each `DeadLettersInfo` entry contains the failed notification, the exception, `AttemptCount` (the total number of times the handler was attempted, including retries), `HandlerType` (the handler that failed, or `null` when the failure happened before any handler ran) and the UTC time of the last attempt. Entries are also written for notifications abandoned during a forced shutdown, with an `OperationCanceledException`, so nothing is silently lost.

For a typed, push-based alternative to polling the queue, see [Dead Letter Handlers](#dead-letter-handlers). The queue keeps recording every entry whether or not a handler is registered.

## Basic Usage

### Step 1: Define a Request

Create a class that implements `IRequest<TResponse>` where `TResponse` is the type of the response you expect.

```csharp
public class Ping : IRequest<string>
{
    public string Message { get; set; } = "Ping";
}
```

### Step 2: Create a Handler

Implement `IRequestHandler<TRequest, TResponse>` to handle your request.

```csharp
public class PingHandler : IRequestHandler<Ping, string>
{
    public Task<string> Handle(Ping request, CancellationToken cancellationToken)
    {
        return Task.FromResult($"{request.Message} Pong");
    }
}
```

### Step 3: Register the Handler

Register your handler with the dependency injection container.

```csharp
services.AddRequestHandler<Ping, string, PingHandler>();
```

### Step 4: Send the Request

Use the `IMediator` interface to send your request.

```csharp
var mediator = provider.GetRequiredService<IMediator>();
var response = await mediator.Send(new Ping { Message = "Hello" });
// response = "Hello Pong"
```



## Streams

MediatRR supports `IAsyncEnumerable` streams, allowing you to stream data from your handlers.

### Defining a Stream Request

Create a class that implements `IStreamRequest<TResponse>`:

```csharp
public class StreamData : IStreamRequest<int>
{
    public int Count { get; set; }
}
```

### Creating a Stream Handler

Implement `IStreamRequestHandler<TRequest, TResponse>` to handle your stream request:

```csharp
public class StreamDataHandler : IStreamRequestHandler<StreamData, int>
{
    public async IAsyncEnumerable<int> Handle(StreamData request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (int i = 0; i < request.Count; i++)
        {
            await Task.Delay(100, cancellationToken);
            yield return i;
        }
    }
}
```

### Registering Stream Handlers

You can register handlers manually or use the auto-registration feature.

#### Manual Registration

```csharp
services.AddStreamRequestHandler<StreamData, int, StreamDataHandler>();
```

#### Auto-Registration

MediatRR ships a source generator inside the package. It registers every non-abstract, non-generic class or record in your compilation that implements `IRequestHandler<,>`, `IStreamRequestHandler<,>` or `IDeadLetterHandler<>`, including classes that implement several handler interfaces. Nested types must be at least `internal`. The generated code targets C# 7.3, so it compiles in any project that references the package.

```csharp
using MediatRR.ServiceGenerator;

services.AutoRegisterRequestHandlers();
services.AutoRegisterStreamHandlers();
services.AutoRegisterDeadLetterHandlers();
```

Notification handlers are not auto-registered because each registration may carry a retry policy; register them with `AddNotificationHandler`.

### Consuming the Stream

Use the `CreateStream` method on the `IMediator` interface:

```csharp
var request = new StreamData { Count = 5 };

await foreach (var item in mediator.CreateStream(request))
{
    Console.WriteLine($"Received: {item}");
}
```

## Notifications

Notifications in MediatRR allow you to publish events to multiple handlers. Unlike requests, notifications don't return a value and can have zero or more handlers.

### Defining a Notification

Create a class that implements `INotification`:

```csharp
public class OrderPlaced : INotification
{
    public string OrderId { get; set; }
    public decimal Amount { get; set; }
}
```

### Creating Handlers

You can create multiple handlers for the same notification. Each handler will be executed when the notification is published:

```csharp
public class SendEmailHandler : INotificationHandler<OrderPlaced>
{
    public Task Handle(OrderPlaced notification, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Sending confirmation for order {notification.OrderId}");
        return Task.CompletedTask;
    }
}

public class UpdateInventoryHandler : INotificationHandler<OrderPlaced>
{
    public Task Handle(OrderPlaced notification, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Updating stock for order {notification.OrderId}");
        return Task.CompletedTask;
    }
}
```

### Registering Handlers

Register your notification handlers with the DI container. The retry policy is optional. Omitting it (or passing `null`) means the handler has no opinion: the notification type uses whichever policy its other handlers registered, or no retries at all:

```csharp
var retryPolicy = new NotificationRetryPolicy
{
    MaxRetryAttempts = 3,
    DelayBetweenRetries = TimeSpan.FromSeconds(1)
};

services.AddNotificationHandler<OrderPlaced, SendEmailHandler>(retryPolicy);
services.AddNotificationHandler<OrderPlaced, UpdateInventoryHandler>(retryPolicy);

// Or with no retry policy:
services.AddNotificationHandler<OrderPlaced, AuditHandler>();
```

> **One policy per notification type.** All handlers for the same notification share a single retry policy. Registering two different policies for one notification type throws `InvalidOperationException` when the worker starts. Handlers registered without a policy never conflict.

### Retries

A failing handler is retried in place: after `DelayBetweenRetries` the same handler runs again, up to `MaxRetryAttempts` times. Other handlers of the same notification are not re-run. An exception thrown by an `INotificationHandlerBehavior` is retried the same way. After the last attempt the notification is dead-lettered together with the last exception.

#### Backoff and exception filtering

```csharp
var policy = new NotificationRetryPolicy
{
    MaxRetryAttempts = 5,
    DelayBetweenRetries = TimeSpan.FromSeconds(1),
    BackoffMultiplier = 2,                              // 1s, 2s, 4s, 8s, ...
    MaxDelayBetweenRetries = TimeSpan.FromSeconds(10),  // never wait longer than this
    ShouldRetry = ex => ex is TimeoutException or HttpRequestException
};
```

- `BackoffMultiplier` scales the delay after every retry. The default of `1` keeps the delay constant. Values below `1` are rejected.
- `MaxDelayBetweenRetries` caps a backed-off delay. `null` (the default) means no cap.
- `ShouldRetry` decides per exception whether a retry is worth it. `null` (the default) retries every exception. When it returns `false` the notification is dead-lettered right away, with the attempt count reached so far.
- `GetRetryDelay(failedAttempt)` returns the delay the worker will wait after the given 1-based attempt, if you want to inspect a policy.

All of these take part in the one-policy-per-notification-type check. Two policies with different `ShouldRetry` delegates count as different policies, so share one instance between handlers.

### Dead Letter Handlers

Implement `IDeadLetterHandler<TNotification>` to be called when a notification of that type is dead-lettered. The handler receives a `DeadLetter<TNotification>` with the typed notification, the exception, `AttemptCount`, `HandlerType` and `LastAttemptedAt`:

```csharp
public class OrderPlacedDeadLetterHandler : IDeadLetterHandler<OrderPlaced>
{
    public Task Handle(DeadLetter<OrderPlaced> deadLetter, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Order {deadLetter.Notification.OrderId} failed in {deadLetter.HandlerType?.Name} after {deadLetter.AttemptCount} attempts: {deadLetter.Exception.Message}");
        return Task.CompletedTask;
    }
}

services.AddDeadLetterHandler<OrderPlaced, OrderPlacedDeadLetterHandler>();
```

Rules:

- The dead letter queue passed to `AddMediatRR` still records every entry, so existing code keeps working unchanged.
- A dead letter handler runs in the same DI scope as the handler that failed, so scoped services are shared.
- It is called once per failed handler. Several dead letter handlers for the same notification type all run, one after the other.
- It is never retried. If it throws, a second queue entry is written whose `HandlerType` is the dead letter handler's type and whose exception is the one it threw.
- It also runs when handler resolution fails before any handler executes; `HandlerType` is `null` and `AttemptCount` is `0` in that case.
- During shutdown, notifications that are cancelled in flight or never read from the channel go to the queue only. Dead letter handlers are not invoked once the host is stopping.

### Ordered Notifications

By default every notification is handled as soon as a concurrency slot is free, so two notifications published back to back may be handled in either order. When order matters within a group, for example all events of one account, implement `IOrderedNotification` instead of `INotification`:

```csharp
public class BalanceChanged : IOrderedNotification
{
    public string AccountId { get; set; }
    public decimal NewBalance { get; set; }
    public string OrderingKey => AccountId;
}
```

Notifications with the same `OrderingKey` are handled strictly in publish order: the next one starts only after every handler of the previous one has finished, retries included. Notifications with different keys are handled concurrently, as are plain notifications. A `null` key opts that notification out of ordering.

Ordered notifications are taken off the channel ahead of time so that other keys are not held up. At most `NotificationChannelSize` of them wait for their key in memory; beyond that `Publish` waits, the same way it does when the channel is full.

### Publishing Notifications

Use the `Publish` method to send notifications to all registered handlers:

```csharp
await mediator.Publish(new OrderPlaced 
{ 
    OrderId = "ORD-12345", 
    Amount = 99.99m 
});
```

`Publish` returns once the notification is queued, after the `INotificationBehavior` pipeline has run. After the host has stopped, `Publish` throws `InvalidOperationException`.

## Behaviors

Behaviors allow you to add cross-cutting concerns to your pipeline.

### Pipeline Behaviors

Pipeline behaviors wrap around request handlers.

```csharp
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> 
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, Func<Task<TResponse>> next, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[Log] Handling {typeof(TRequest).Name}");
        var response = await next();
        Console.WriteLine($"[Log] Handled {typeof(TRequest).Name}");
        return response;
    }
}

// Register the behavior
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
```

### Notification Behaviors

MediatRR provides two types of behaviors for notifications:

1. **`INotificationBehavior<TNotification>`**: Wraps the entire notification publishing process. Executes once per `Publish`.
2. **`INotificationHandlerBehavior<TNotification>`**: Wraps each individual handler execution. Executes once per handler.

```csharp
// Wraps the entire publishing process
public class NotificationLoggingBehavior<TNotification> : INotificationBehavior<TNotification>
    where TNotification : INotification
{
    public async Task Handle(TNotification notification, Func<Task> next, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Before publishing {typeof(TNotification).Name}");
        await next();
        Console.WriteLine($"After publishing {typeof(TNotification).Name}");
    }
}

// Wraps individual handler execution
public class NotificationHandlerLoggingBehavior<TNotification> : INotificationHandlerBehavior<TNotification>
    where TNotification : INotification
{
    public async Task Handle(TNotification notification, Func<Task> next, CancellationToken cancellationToken)
    {
        Console.WriteLine("Before handler execution");
        await next();
        Console.WriteLine("After handler execution");
    }
}

// Register behaviors
services.AddTransient(typeof(INotificationBehavior<>), typeof(NotificationLoggingBehavior<>));
services.AddTransient(typeof(INotificationHandlerBehavior<>), typeof(NotificationHandlerLoggingBehavior<>));
```

### Stream Behaviors

Stream behaviors wrap around stream request handlers, allowing you to intercept the stream creation or iterate the results.

```csharp
public class StreamLoggingBehavior<TRequest, TResponse> : IStreamBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async IAsyncEnumerable<TResponse> Handle(TRequest request, Func<IAsyncEnumerable<TResponse>> next, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Console.WriteLine($"[Stream Log] Starting stream for {typeof(TRequest).Name}");
        
        var stream = next();
        
        await foreach (var item in stream.WithCancellation(cancellationToken))
        {
            Console.WriteLine($"[Stream Log] Received item: {item}");
            yield return item;
        }
        
        Console.WriteLine($"[Stream Log] Completed stream for {typeof(TRequest).Name}");
    }
}

// Register the behavior
services.AddTransient(typeof(IStreamBehavior<,>), typeof(StreamLoggingBehavior<,>));
```

## Metrics

MediatRR emits metrics through `System.Diagnostics.Metrics` on a meter named `MediatRR` (`MediatRRInstrumentation.MeterName`). If an `IMeterFactory` is registered, for example by `services.AddMetrics()` or OpenTelemetry, the meter is created through it. Otherwise MediatRR creates its own.

| Instrument | Kind | Tags | Meaning |
|---|---|---|---|
| `mediatrr.notifications.published` | counter | `notification.type` | Notifications accepted by `Publish` and queued for the worker |
| `mediatrr.notifications.queued` | observable gauge | | Notifications waiting in the channel. Growth here means handlers cannot keep up |
| `mediatrr.handlers.in_flight` | up-down counter | | Handler attempts currently running |
| `mediatrr.handlers.duration` | histogram (ms) | `notification.type`, `handler.type`, `outcome` | Duration of one handler attempt. `outcome` is `success`, `failure` or `cancelled` |
| `mediatrr.handlers.retries` | counter | `notification.type`, `handler.type` | Failed attempts that are retried |
| `mediatrr.dead_letters` | counter | `notification.type`, `handler.type` | Entries written to the dead letter queue. `handler.type` is omitted when no handler ran |

With OpenTelemetry:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter(MediatRRInstrumentation.MeterName));
```

## ASP.NET Core Integration

In an ASP.NET Core application, register MediatRR in your `Program.cs` or `Startup.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);

var deadLetters = new ConcurrentQueue<DeadLettersInfo>();
builder.Services.AddMediatRR(cfg => { }, deadLetters);

// Register your handlers
builder.Services.AddRequestHandler<MyRequest, MyResponse, MyRequestHandler>();

var app = builder.Build();
```