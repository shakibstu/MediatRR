using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MediatRR.Tests.Regression;

internal static class WorkerHost
{
    public static async Task<(ServiceProvider Provider, BackgroundService Worker, IMediator Mediator)> StartAsync(ServiceCollection services)
    {
        var provider = services.BuildServiceProvider();
        var worker = (BackgroundService)provider.GetRequiredService<IHostedService>();
        await worker.StartAsync(CancellationToken.None);
        return (provider, worker, provider.GetRequiredService<IMediator>());
    }

    public static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }
    }
}
