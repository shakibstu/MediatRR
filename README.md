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

`IMediator` is registered as transient. `Send` and `CreateStream` each open their own DI scope — shared between the pipeline behaviors and the handler — so scoped dependencies (e.g. a per-request `DbContext`) work correctly. For `CreateStream` that scope lives until the returned `IAsyncEnumerable` is fully enumerated or its enumerator is disposed. `Publish` opens a scope for its notification behaviors and then queues the notification; each handler runs later in the background worker, in its own per-message scope.

### Configuration Options

The `AddMediatRR` method accepts a configuration action with the following options:

- `NotificationChannelSize`: The size of the notification channel buffer (default: 10,000)
- `MaxConcurrentMessageConsumer`: Maximum concurrent notification handlers (default: 5)

### Dead Letter Queue

The `deadLetters` parameter is a `ConcurrentQueue<DeadLettersInfo>` that collects notifications that failed to process after all retry attempts. This allows you to:

- Monitor and log failed notifications
- Implement custom retry logic or manual intervention
- Analyze patterns in notification failures
- Ensure no notifications are silently lost

Each `DeadLettersInfo` entry contains the failed notification and error details, allowing you to investigate and potentially reprocess failed messages.

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

MediatRR includes a source generator that can automatically register your stream handlers. This requires importing the generated namespace:

```csharp
using MediatRR.ServiceGenerator;

// Registers all stream handlers in the assembly
services.AutoRegisterStreamHandlers();
```

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

Register your notification handlers with the DI container. The retry policy is optional — omit it (or pass `null`) to use `NotificationRetryPolicy.Default` (zero retries, no delay):

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

> **One policy per notification type.** All handlers for the same notification share a single retry policy. Registering conflicting policies for the same notification type throws `InvalidOperationException` when the resiliency provider is resolved. Registering the same (or equivalent) policy multiple times is fine.

### Publishing Notifications

Use the `Publish` method to send notifications to all registered handlers:

```csharp
await mediator.Publish(new OrderPlaced 
{ 
    OrderId = "ORD-12345", 
    Amount = 99.99m 
});
```

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