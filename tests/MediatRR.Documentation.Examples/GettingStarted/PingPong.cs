using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;

namespace MediatRR.Documentation.Examples.GettingStarted
{
    // 1. Define the Request
    public class Ping : IRequest<string>
    {
        public string Message { get; set; } = "Ping";
    }

    // 2. Define the Handler
    public class PingHandler : IRequestHandler<Ping, string>
    {
        public Task<string> Handle(Ping request, CancellationToken cancellationToken)
        {
            return Task.FromResult($"{request.Message} Pong");
        }
    }

    // 3. Run the Example
    public static class PingPongRunner
    {
        public static async Task Run()
        {
            Console.WriteLine("Running PingPong Example...");

            var services = new ServiceCollection();
            var deadLetters = new ConcurrentQueue<DeadLettersInfo>();

            // Register MediatRR
            services.AddMediatRR(cfg => { }, deadLetters);

            // Register Handler
            services.AddRequestHandler<Ping, string, PingHandler>();

            var provider = services.BuildServiceProvider();
            var mediator = provider.GetRequiredService<IMediator>();

            var response = await mediator.Send(new Ping { Message = "Hello" });

            Console.WriteLine($"Response: {response}");
        }
    }
}
