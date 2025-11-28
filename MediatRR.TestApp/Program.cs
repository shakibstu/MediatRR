using MediatRR.TestApp.Controllers;
using System.Collections.Concurrent;
using MediatRR.ServiceGenerator;

namespace MediatRR.TestApp
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            var c = new ConcurrentQueue<DeadLettersInfo>();
            builder.Services.AddControllers();
            builder.Services.AutoRegisterRequestHandlers();
            builder.Services.AddMediatRR(a => a.NotificationChannelSize = 100, c);
            builder.Services.AddSingleton(c);
            var app = builder.Build();

            // Configure the HTTP request pipeline.

            app.UseHttpsRedirection();

            app.UseAuthorization();


            app.MapControllers();

            app.Run();
        }
    }
}
