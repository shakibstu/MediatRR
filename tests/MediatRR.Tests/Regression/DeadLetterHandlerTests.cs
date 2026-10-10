using System.Collections.Concurrent;
using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace MediatRR.Tests.Regression;

public class DeadLetterHandlerTests
{
    private static readonly NotificationRetryPolicy OneRetry = new() { MaxRetryAttempts = 1 };

    public sealed class Note : INotification { public int Id { get; set; } }

    public sealed class BadHandler : INotificationHandler<Note>
    {
        public Task Handle(Note notification, CancellationToken cancellationToken) => throw new InvalidOperationException("bad");
    }

    public sealed class GoodHandler : INotificationHandler<Note>
    {
        public Task Handle(Note notification, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class RecordingDeadLetterHandler : IDeadLetterHandler<Note>
    {
        public static readonly ConcurrentQueue<DeadLetter<Note>> Seen = new();

        public Task Handle(DeadLetter<Note> deadLetter, CancellationToken cancellationToken)
        {
            Seen.Enqueue(deadLetter);
            return Task.CompletedTask;
        }
    }

    public sealed class ThrowingDeadLetterHandler : IDeadLetterHandler<Note>
    {
        public Task Handle(DeadLetter<Note> deadLetter, CancellationToken cancellationToken) => throw new ApplicationException("dead letter handler failed");
    }

    public sealed class ScopeMarker { public Guid Id { get; } = Guid.NewGuid(); }

    public sealed class ScopedBadHandler : INotificationHandler<Note>
    {
        public static Guid Seen;
        private readonly ScopeMarker _marker;

        public ScopedBadHandler(ScopeMarker marker) => _marker = marker;

        public Task Handle(Note notification, CancellationToken cancellationToken)
        {
            Seen = _marker.Id;
            throw new InvalidOperationException("scoped bad");
        }
    }

    public sealed class ScopedDeadLetterHandler : IDeadLetterHandler<Note>
    {
        public static Guid Seen;
        private readonly ScopeMarker _marker;

        public ScopedDeadLetterHandler(ScopeMarker marker) => _marker = marker;

        public Task Handle(DeadLetter<Note> deadLetter, CancellationToken cancellationToken)
        {
            Seen = _marker.Id;
            return Task.CompletedTask;
        }
    }

    public interface IMissing { }

    public sealed class Broken : INotification { }

    public sealed class NeedsMissing : INotificationHandler<Broken>
    {
        private readonly IMissing _missing;

        public NeedsMissing(IMissing missing) => _missing = missing;

        public Task Handle(Broken notification, CancellationToken cancellationToken) => Task.FromResult(_missing);
    }

    public sealed class BrokenDeadLetterHandler : IDeadLetterHandler<Broken>
    {
        public static DeadLetter<Broken>? Seen;

        public Task Handle(DeadLetter<Broken> deadLetter, CancellationToken cancellationToken)
        {
            Seen = deadLetter;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Typed_dead_letter_handler_receives_the_failure_and_the_queue_still_records_it()
    {
        RecordingDeadLetterHandler.Seen.Clear();
        var deadLetters = new ConcurrentQueue<DeadLettersInfo>();
        var services = new ServiceCollection();
        services.AddMediatRR(_ => { }, deadLetters);
        services.AddNotificationHandler<Note, GoodHandler>(OneRetry);
        services.AddNotificationHandler<Note, BadHandler>(OneRetry);
        services.AddDeadLetterHandler<Note, RecordingDeadLetterHandler>();
        var (_, worker, mediator) = await WorkerHost.StartAsync(services);

        await mediator.Publish(new Note { Id = 42 });
        await worker.StopAsync(CancellationToken.None);

        var seen = Assert.Single(RecordingDeadLetterHandler.Seen);
        Assert.Equal(42, seen.Notification.Id);
        Assert.IsType<InvalidOperationException>(seen.Exception);
        Assert.Equal(2, seen.AttemptCount);
        Assert.Equal(typeof(BadHandler), seen.HandlerType);
        Assert.Equal(DateTimeKind.Utc, seen.LastAttemptedAt.Kind);

        var queued = Assert.Single(deadLetters);
        Assert.Same(seen.Notification, queued.Message);
        Assert.Equal(typeof(BadHandler), queued.HandlerType);
        Assert.Equal(2, queued.AttemptCount);
    }

    [Fact]
    public async Task A_throwing_dead_letter_handler_adds_its_own_queue_entry_and_does_not_lose_the_original()
    {
        var deadLetters = new ConcurrentQueue<DeadLettersInfo>();
        var services = new ServiceCollection();
        services.AddMediatRR(_ => { }, deadLetters);
        services.AddNotificationHandler<Note, BadHandler>();
        services.AddDeadLetterHandler<Note, ThrowingDeadLetterHandler>();
        var (_, worker, mediator) = await WorkerHost.StartAsync(services);

        await mediator.Publish(new Note { Id = 7 });
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(2, deadLetters.Count);
        var entries = deadLetters.ToArray();
        Assert.Contains(entries, e => e.HandlerType == typeof(BadHandler) && e.Exception is InvalidOperationException && e.AttemptCount == 1);
        Assert.Contains(entries, e => e.HandlerType == typeof(ThrowingDeadLetterHandler) && e.Exception is ApplicationException && e.AttemptCount == 1);
        Assert.All(entries, e => Assert.Equal(7, ((Note)e.Message).Id));
    }

    [Fact]
    public async Task Dead_letter_handler_resolves_from_the_same_scope_as_the_failed_handler()
    {
        ScopedBadHandler.Seen = Guid.Empty;
        ScopedDeadLetterHandler.Seen = Guid.Empty;
        var services = new ServiceCollection();
        services.AddMediatRR(_ => { }, new ConcurrentQueue<DeadLettersInfo>());
        services.AddScoped<ScopeMarker>();
        services.AddNotificationHandler<Note, ScopedBadHandler>();
        services.AddDeadLetterHandler<Note, ScopedDeadLetterHandler>();
        var (_, worker, mediator) = await WorkerHost.StartAsync(services);

        await mediator.Publish(new Note());
        await worker.StopAsync(CancellationToken.None);

        Assert.NotEqual(Guid.Empty, ScopedBadHandler.Seen);
        Assert.Equal(ScopedBadHandler.Seen, ScopedDeadLetterHandler.Seen);
    }

    [Fact]
    public async Task Dead_letter_handler_runs_when_handler_resolution_fails_with_no_handler_type()
    {
        BrokenDeadLetterHandler.Seen = null;
        var deadLetters = new ConcurrentQueue<DeadLettersInfo>();
        var services = new ServiceCollection();
        services.AddMediatRR(_ => { }, deadLetters);
        services.AddNotificationHandler<Broken, NeedsMissing>();
        services.AddDeadLetterHandler<Broken, BrokenDeadLetterHandler>();
        var (_, worker, mediator) = await WorkerHost.StartAsync(services);

        await mediator.Publish(new Broken());
        await worker.StopAsync(CancellationToken.None);

        Assert.NotNull(BrokenDeadLetterHandler.Seen);
        Assert.Null(BrokenDeadLetterHandler.Seen!.HandlerType);
        Assert.Equal(0, BrokenDeadLetterHandler.Seen.AttemptCount);
        var queued = Assert.Single(deadLetters);
        Assert.Null(queued.HandlerType);
    }

    [Fact]
    public async Task Without_a_dead_letter_handler_the_queue_behaves_as_before()
    {
        var deadLetters = new ConcurrentQueue<DeadLettersInfo>();
        var services = new ServiceCollection();
        services.AddMediatRR(_ => { }, deadLetters);
        services.AddNotificationHandler<Note, BadHandler>();
        var (_, worker, mediator) = await WorkerHost.StartAsync(services);

        await mediator.Publish(new Note());
        await worker.StopAsync(CancellationToken.None);

        var queued = Assert.Single(deadLetters);
        Assert.Equal(typeof(BadHandler), queued.HandlerType);
    }

    [Fact]
    public void Four_argument_DeadLettersInfo_constructor_keeps_working_with_a_null_handler_type()
    {
        var info = new DeadLettersInfo(new Note(), new InvalidOperationException(), 1, DateTime.UtcNow);

        Assert.Null(info.HandlerType);
    }
}
