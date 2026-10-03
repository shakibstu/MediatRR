using System;
using System.Collections.Concurrent;

namespace MediatRR;

/// <summary>
/// Internal container for the dead letter queue.
/// Holds notifications that have failed after exceeding maximum retry attempts.
/// </summary>
internal sealed class InternalDeadLettersKeeper
{
    public InternalDeadLettersKeeper(ConcurrentQueue<DeadLettersInfo> queue)
    {
        DeadLettersQueue = queue ?? throw new ArgumentNullException(nameof(queue));
    }

    public ConcurrentQueue<DeadLettersInfo> DeadLettersQueue { get; }

    public void Record(object message, Exception exception, int attemptCount) =>
        DeadLettersQueue.Enqueue(new DeadLettersInfo(message, exception, attemptCount, DateTime.UtcNow));
}
