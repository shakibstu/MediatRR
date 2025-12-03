using Microsoft.AspNetCore.Mvc;
using System.Collections.Concurrent;

namespace MediatRR.TestApp.Controllers;


[ApiController]
[Route("[controller]")]
public class WeatherForecastController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ConcurrentQueue<DeadLettersInfo> _concurrentQueue;

    private static readonly string[] Summaries = new[]
    {
            "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
        };


    public WeatherForecastController(IMediator mediator, ConcurrentQueue<DeadLettersInfo> concurrentQueue)
    {
        _mediator = mediator;
        _concurrentQueue = concurrentQueue;
    }

    [HttpGet]
    public async Task<IEnumerable<WeatherForecast>> Get()
    {
        var rng = new Random();
        await _mediator.Publish(new WeatherForecast
        {
            Date = DateTime.Now.AddDays(1),
            TemperatureC = rng.Next(-20, 55),
            Summary = Summaries[rng.Next(Summaries.Length)]
        });
        return Enumerable.Range(1, 5).Select(index => new WeatherForecast
        {
            Date = DateTime.Now.AddDays(index),
            TemperatureC = rng.Next(-20, 55),
            Summary = Summaries[rng.Next(Summaries.Length)]
        })
        .ToArray();
    }

}
