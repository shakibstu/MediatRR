using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace MediatRR.Tests.Regression;

public class MetricsTests
{
    public sealed class Note : INotification { }

    public sealed class Ok : INotificationHandler<Note>
    {
        public Task Handle(Note notification, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class Bad : INotificationHandler<Note>
    {
        public Task Handle(Note notification, CancellationToken cancellationToken) => throw new InvalidOperationException("bad");
    }

    public sealed class Blocking : INotificationHandler<Note>
    {
        public static TaskCompletionSource<bool> Gate = new();

        public Task Handle(Note notification, CancellationToken cancellationToken) => Gate.Task;
    }

    private sealed class Recorder : IDisposable
    {
        private readonly MeterListener _listener = new();
        public readonly ConcurrentQueue<(string Instrument, double Value, Dictionary<string, object?> Tags)> Measurements = new();

        public Recorder(Meter meter)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (ReferenceEquals(instrument.Meter, meter)) listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
            _listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) => Record(instrument, value, tags));
            _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
            _listener.Start();
        }

        private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var dictionary = new Dictionary<string, object?>();
            foreach (var tag in tags) dictionary[tag.Key] = tag.Value;
            Measurements.Enqueue((instrument.Name, value, dictionary));
        }

        public void Observe() => _listener.RecordObservableInstruments();

        public IEnumerable<(string Instrument, double Value, Dictionary<string, object?> Tags)> For(string instrument) =>
            Measurements.Where(m => m.Instrument == instrument);

        public void Dispose() => _listener.Dispose();
    }

    private static async Task<(Recorder Recorder, Microsoft.Extensions.Hosting.BackgroundService Worker, IMediator Mediator)> StartAsync(ServiceCollection services)
    {
        var provider = services.BuildServiceProvider();
        var recorder = new Recorder(provider.GetRequiredService<MediatRRMetrics>().Meter);
        var worker = (Microsoft.Extensions.Hosting.BackgroundService)provider.GetRequiredService<Microsoft.Extensions.Hosting.IHostedService>();
        await worker.StartAsync(CancellationToken.None);
        return (recorder, worker, provider.GetRequiredService<IMediator>());
    }

    [Fact]
    public async Task Published_notifications_are_counted_by_type()
    {
        var services = new ServiceCollection();
        services.AddMediatRR(_ => { }, new ConcurrentQueue<DeadLettersInfo>());
        services.AddNotificationHandler<Note, Ok>();
        var (recorder, worker, mediator) = await StartAsync(services);
        using (recorder)
        {
            await mediator.Publish(new Note());
            await mediator.Publish(new Note());
            await worker.StopAsync(CancellationToken.None);

            var published = recorder.For("mediatrr.notifications.published").ToList();
            Assert.Equal(2, published.Sum(m => m.Value));
            Assert.All(published, m => Assert.Equal(typeof(Note).FullName, m.Tags["notification.type"]));
        }
    }

    [Fact]
    public async Task Retries_and_dead_letters_are_counted_with_handler_type()
    {
        var services = new ServiceCollection();
        services.AddMediatRR(_ => { }, new ConcurrentQueue<DeadLettersInfo>());
        services.AddNotificationHandler<Note, Bad>(new NotificationRetryPolicy { MaxRetryAttempts = 2 });
        var (recorder, worker, mediator) = await StartAsync(services);
        using (recorder)
        {
            await mediator.Publish(new Note());
            await worker.StopAsync(CancellationToken.None);

            Assert.Equal(2, recorder.For("mediatrr.handlers.retries").Sum(m => m.Value));
            var deadLetter = Assert.Single(recorder.For("mediatrr.dead_letters"));
            Assert.Equal(1, deadLetter.Value);
            Assert.Equal(typeof(Note).FullName, deadLetter.Tags["notification.type"]);
            Assert.Equal(typeof(Bad).FullName, deadLetter.Tags["handler.type"]);
        }
    }

    [Fact]
    public async Task Handler_duration_is_recorded_per_attempt_with_outcome()
    {
        var services = new ServiceCollection();
        services.AddMediatRR(_ => { }, new ConcurrentQueue<DeadLettersInfo>());
        services.AddNotificationHandler<Note, Ok>();
        services.AddNotificationHandler<Note, Bad>();
        var (recorder, worker, mediator) = await StartAsync(services);
        using (recorder)
        {
            await mediator.Publish(new Note());
            await worker.StopAsync(CancellationToken.None);

            var durations = recorder.For("mediatrr.handlers.duration").ToList();
            Assert.Equal(2, durations.Count);
            Assert.Contains(durations, m => Equals(m.Tags["handler.type"], typeof(Ok).FullName) && Equals(m.Tags["outcome"], "success"));
            Assert.Contains(durations, m => Equals(m.Tags["handler.type"], typeof(Bad).FullName) && Equals(m.Tags["outcome"], "failure"));
            Assert.All(durations, m => Assert.True(m.Value >= 0));
        }
    }

    [Fact]
    public async Task In_flight_handlers_rise_while_running_and_return_to_zero()
    {
        Blocking.Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new ServiceCollection();
        services.AddMediatRR(_ => { }, new ConcurrentQueue<DeadLettersInfo>());
        services.AddNotificationHandler<Note, Blocking>();
        var (recorder, worker, mediator) = await StartAsync(services);
        using (recorder)
        {
            await mediator.Publish(new Note());
            await WorkerHost.WaitUntilAsync(() => recorder.For("mediatrr.handlers.in_flight").Sum(m => m.Value) == 1, TimeSpan.FromSeconds(2));
            Assert.Equal(1, recorder.For("mediatrr.handlers.in_flight").Sum(m => m.Value));

            Blocking.Gate.SetResult(true);
            await worker.StopAsync(CancellationToken.None);

            Assert.Equal(0, recorder.For("mediatrr.handlers.in_flight").Sum(m => m.Value));
        }
    }

    [Fact]
    public async Task Queued_gauge_reports_notifications_still_in_the_channel()
    {
        Blocking.Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new ServiceCollection();
        services.AddMediatRR(cfg => cfg.MaxConcurrentMessageConsumer = 1, new ConcurrentQueue<DeadLettersInfo>());
        services.AddNotificationHandler<Note, Blocking>();
        var (recorder, worker, mediator) = await StartAsync(services);
        using (recorder)
        {
            for (var i = 0; i < 3; i++) await mediator.Publish(new Note());

            double Depth()
            {
                recorder.Observe();
                return recorder.For("mediatrr.notifications.queued").LastOrDefault().Value;
            }

            await WorkerHost.WaitUntilAsync(() => Depth() == 1, TimeSpan.FromSeconds(2));
            Assert.Equal(1, Depth());

            Blocking.Gate.SetResult(true);
            await worker.StopAsync(CancellationToken.None);
            Assert.Equal(0, Depth());
        }
    }

    private sealed class RecordingMeterFactory : IMeterFactory
    {
        public readonly List<string> Names = new();

        public Meter Create(MeterOptions options)
        {
            Names.Add(options.Name);
            return new Meter(options);
        }

        public void Dispose() { }
    }

    [Fact]
    public void The_meter_is_created_through_IMeterFactory_when_one_is_registered()
    {
        var factory = new RecordingMeterFactory();
        var services = new ServiceCollection();
        services.AddSingleton<IMeterFactory>(factory);
        services.AddMediatRR(_ => { }, new ConcurrentQueue<DeadLettersInfo>());

        var metrics = services.BuildServiceProvider().GetRequiredService<MediatRRMetrics>();

        Assert.Equal(MediatRRInstrumentation.MeterName, Assert.Single(factory.Names));
        Assert.Equal(MediatRRInstrumentation.MeterName, metrics.Meter.Name);
    }
}
