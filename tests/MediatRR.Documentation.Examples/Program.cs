using MediatRR.Documentation.Examples.GettingStarted;

namespace MediatRR.Documentation.Examples
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("MediatRR Documentation Examples");
            Console.WriteLine("===============================");

            await PingPongRunner.Run();
            await Features.LoggingBehaviorRunner.Run();
            await Features.NotificationRunner.Run();
            await Features.NotificationBehaviorRunner.Run();

            Console.WriteLine("\nDone.");
        }
    }
}
