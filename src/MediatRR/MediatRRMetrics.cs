using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MediatRR;

/// <summary>
/// Names used by the metrics MediatRR emits through <see cref="System.Diagnostics.Metrics"/>.
/// </summary>
public static class MediatRRInstrumentation
{
    /// <summary>
    /// The name of the <see cref="Meter"/>. Subscribe to it with a <see cref="MeterListener"/> or an OpenTelemetry meter provider.
    /// </summary>
    public const string MeterName = "MediatRR";
}

internal sealed class MediatRRMetrics : IDisposable
{
    private const string NotificationTypeTag = "notification.type";
    private const string HandlerTypeTag = "handler.type";
    private const string OutcomeTag = "outcome";

    private readonly bool _ownsMeter;
    private readonly Counter<long> _published;
    private readonly UpDownCounter<long> _inFlight;
    private readonly Counter<long> _retries;
    private readonly Counter<long> _deadLetters;
    private readonly Histogram<double> _duration;

    public MediatRRMetrics(NotificationChannel channel, IMeterFactory meterFactory = null)
    {
        var version = typeof(MediatRRMetrics).Assembly.GetName().Version?.ToString();
        if (meterFactory is null)
        {
            Meter = new Meter(MediatRRInstrumentation.MeterName, version);
            _ownsMeter = true;
        }
        else
        {
            Meter = meterFactory.Create(new MeterOptions(MediatRRInstrumentation.MeterName) { Version = version });
        }

        _published = Meter.CreateCounter<long>("mediatrr.notifications.published", "{notification}", "Notifications accepted by Publish and queued for the worker.");
        Meter.CreateObservableGauge("mediatrr.notifications.queued", () => channel.Count, "{notification}", "Notifications waiting in the channel.");
        _inFlight = Meter.CreateUpDownCounter<long>("mediatrr.handlers.in_flight", "{handler}", "Handler attempts currently running.");
        _retries = Meter.CreateCounter<long>("mediatrr.handlers.retries", "{retry}", "Handler attempts that failed and are retried.");
        _deadLetters = Meter.CreateCounter<long>("mediatrr.dead_letters", "{entry}", "Entries written to the dead letter queue.");
        _duration = Meter.CreateHistogram<double>("mediatrr.handlers.duration", "ms", "Duration of one handler attempt.");
    }

    public Meter Meter { get; }

    public void NotificationPublished(Type notificationType) =>
        _published.Add(1, new KeyValuePair<string, object>(NotificationTypeTag, notificationType.FullName));

    public long HandlerStarted()
    {
        _inFlight.Add(1);
        return Stopwatch.GetTimestamp();
    }

    public void HandlerFinished(long startedAt, Type notificationType, Type handlerType, string outcome)
    {
        _inFlight.Add(-1);
        var elapsedMilliseconds = (Stopwatch.GetTimestamp() - startedAt) * 1000.0 / Stopwatch.Frequency;
        _duration.Record(elapsedMilliseconds,
            new KeyValuePair<string, object>(NotificationTypeTag, notificationType.FullName),
            new KeyValuePair<string, object>(HandlerTypeTag, handlerType.FullName),
            new KeyValuePair<string, object>(OutcomeTag, outcome));
    }

    public void HandlerRetried(Type notificationType, Type handlerType) =>
        _retries.Add(1,
            new KeyValuePair<string, object>(NotificationTypeTag, notificationType.FullName),
            new KeyValuePair<string, object>(HandlerTypeTag, handlerType.FullName));

    public void DeadLettered(Type notificationType, Type handlerType)
    {
        if (handlerType is null)
        {
            _deadLetters.Add(1, new KeyValuePair<string, object>(NotificationTypeTag, notificationType.FullName));
            return;
        }

        _deadLetters.Add(1,
            new KeyValuePair<string, object>(NotificationTypeTag, notificationType.FullName),
            new KeyValuePair<string, object>(HandlerTypeTag, handlerType.FullName));
    }

    public void Dispose()
    {
        if (_ownsMeter)
        {
            Meter.Dispose();
        }
    }
}
