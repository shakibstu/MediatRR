using MediatRR.Contract.Messaging;
using MediatRR.TestApp.Controllers;

namespace MediatRR.TestApp
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            builder.Services.AddControllers();
            builder.Services.AddTransient<INotificationHandler<WeatherForecast>, WeatherGetHandler>();
            builder.Services.AddMediatRR(a=>a.NotificationChannelSize = 100);
            var app = builder.Build();

            // Configure the HTTP request pipeline.

            app.UseHttpsRedirection();

            app.UseAuthorization();


            app.MapControllers();

            app.Run();
        }
    }
}
