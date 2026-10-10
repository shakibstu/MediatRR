using System;
using System.Collections.Concurrent;

namespace MediatRR;

/// <summary>
/// Internal container for the dead letter queue.
/// Holds notifications that have failed after exceeding maximum retry attempts.
/// </summary>
internal sealed class InternalDeadLettersKeeper
{
    private readonly MediatRRMetrics _metrics;

    public InternalDeadLettersKeeper(ConcurrentQueue<DeadLettersInfo> queue, MediatRRMetrics metrics)
    {
        DeadLettersQueue = queue ?? throw new ArgumentNullException(nameof(queue));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    public ConcurrentQueue<DeadLettersInfo> DeadLettersQueue { get; }

    public DeadLettersInfo Record(object message, Exception exception, int attemptCount, Type handlerType = null)
    {
        var info = new DeadLettersInfo(message, exception, attemptCount, DateTime.UtcNow, handlerType);
        DeadLettersQueue.Enqueue(info);
        _metrics.DeadLettered(message.GetType(), handlerType);
        return info;
    }
}
