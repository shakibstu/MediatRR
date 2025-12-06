using MediatRR.Contract.Messaging;
using System.Threading;

namespace MediatRR.TestApp.Controllers
{
    public class WeatherGetHandler : INotificationHandler<WeatherForecast>
    {

        public Task Handle(WeatherForecast notification, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    public class WeatherGetHandler1 : INotificationHandler<WeatherForecast>
    {

        public Task Handle(WeatherForecast notification, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    public class WeatherStreamGetHandler : IStreamRequestHandler<WeatherForecast, object>
    {
        public async IAsyncEnumerable<object> Handle(WeatherForecast request, CancellationToken ct)
        {
            int count = 0;
            while (count < 5)
            {
                await Task.Delay(500, ct);
                yield return count;
                count++;
            }
        }
    }
}
