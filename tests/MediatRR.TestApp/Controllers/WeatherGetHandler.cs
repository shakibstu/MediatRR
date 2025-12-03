using MediatRR.Contract.Messaging;

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
}
