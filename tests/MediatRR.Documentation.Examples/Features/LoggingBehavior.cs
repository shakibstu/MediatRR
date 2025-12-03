using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;

namespace MediatRR.Documentation.Examples.Features
{
    // 1. Define the Behavior
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

    // 2. Define Request/Handler (Reusing Ping/Pong concept but specific for this example)
    public class LoggedPing : IRequest<string>
    {
        public string Message { get; init; }
    }

    public class LoggedPingHandler : IRequestHandler<LoggedPing, string>
    {
        public Task<string> Handle(LoggedPing request, CancellationToken cancellationToken)
        {
            return Task.FromResult($"Logged Pong: {request.Message}");
        }
    }

    // 3. Runner
    public static class LoggingBehaviorRunner
    {
        public static async Task Run()
        {
            Console.WriteLine("\nRunning LoggingBehavior Example...");

            var services = new ServiceCollection();
            var deadLetters = new ConcurrentQueue<DeadLettersInfo>();

            services.AddMediatRR(cfg => { }, deadLetters);

            // Register Behavior
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

            // Register Handler
            services.AddRequestHandler<LoggedPing, string, LoggedPingHandler>();

            var provider = services.BuildServiceProvider();
            var mediator = provider.GetRequiredService<IMediator>();

            var response = await mediator.Send(new LoggedPing { Message = "Test" });

            Console.WriteLine($"Response: {response}");
        }
    }
}
