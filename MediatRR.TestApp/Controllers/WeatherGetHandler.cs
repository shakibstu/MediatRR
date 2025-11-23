using MediatRR.Contract.Messaging;

namespace MediatRR.TestApp.Controllers
{
    public class WeatherGetHandler : INotificationHandler<WeatherForecast>
    {

        public Task Handle(WeatherForecast notification, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
