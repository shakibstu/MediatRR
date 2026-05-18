using MediatRR.Contract.Messaging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace MediatRR;

internal static class HandlerCache
{
    private static readonly ConcurrentDictionary<(Type Req, Type Resp), Type> RequestHandlerTypes = new();
    private static readonly ConcurrentDictionary<(Type Req, Type Resp), Type> StreamRequestHandlerTypes = new();
    private static readonly ConcurrentDictionary<(Type Req, Type Resp), Type> PipelineBehaviorTypes = new();
    private static readonly ConcurrentDictionary<(Type Req, Type Resp), Type> StreamBehaviorTypes = new();
    private static readonly ConcurrentDictionary<Type, Type> NotificationHandlerTypes = new();
    private static readonly ConcurrentDictionary<Type, Type> NotificationBehaviorTypes = new();
    private static readonly ConcurrentDictionary<Type, Type> NotificationHandlerBehaviorTypes = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo> HandleMethods = new();

    public static Type RequestHandlerType(Type req, Type resp) =>
        RequestHandlerTypes.GetOrAdd((req, resp), static k => typeof(IRequestHandler<,>).MakeGenericType(k.Req, k.Resp));

    public static Type StreamRequestHandlerType(Type req, Type resp) =>
        StreamRequestHandlerTypes.GetOrAdd((req, resp), static k => typeof(IStreamRequestHandler<,>).MakeGenericType(k.Req, k.Resp));

    public static Type PipelineBehaviorType(Type req, Type resp) =>
        PipelineBehaviorTypes.GetOrAdd((req, resp), static k => typeof(IPipelineBehavior<,>).MakeGenericType(k.Req, k.Resp));

    public static Type StreamBehaviorType(Type req, Type resp) =>
        StreamBehaviorTypes.GetOrAdd((req, resp), static k => typeof(IStreamBehavior<,>).MakeGenericType(k.Req, k.Resp));

    public static Type NotificationHandlerType(Type notif) =>
        NotificationHandlerTypes.GetOrAdd(notif, static t => typeof(INotificationHandler<>).MakeGenericType(t));

    public static Type NotificationBehaviorType(Type notif) =>
        NotificationBehaviorTypes.GetOrAdd(notif, static t => typeof(INotificationBehavior<>).MakeGenericType(t));

    public static Type NotificationHandlerBehaviorType(Type notif) =>
        NotificationHandlerBehaviorTypes.GetOrAdd(notif, static t => typeof(INotificationHandlerBehavior<>).MakeGenericType(t));

    private static MethodInfo HandleMethod(Type target) =>
        HandleMethods.GetOrAdd(target, static t =>
            t.GetMethod("Handle") ?? throw new InvalidOperationException($"No Handle method on {t}"));

    public static Task<TResponse> InvokeRequestHandler<TResponse>(object handler, object request, CancellationToken ct) =>
        (Task<TResponse>)HandleMethod(handler.GetType()).Invoke(handler, [request, ct]);

    public static Task<TResponse> InvokePipelineBehavior<TResponse>(object behavior, object request, Func<Task<TResponse>> next, CancellationToken ct) =>
        (Task<TResponse>)HandleMethod(behavior.GetType()).Invoke(behavior, [request, next, ct]);

    public static IAsyncEnumerable<TResponse> InvokeStreamHandler<TResponse>(object handler, object request, CancellationToken ct) =>
        (IAsyncEnumerable<TResponse>)HandleMethod(handler.GetType()).Invoke(handler, [request, ct]);

    public static IAsyncEnumerable<TResponse> InvokeStreamBehavior<TResponse>(object behavior, object request, Func<IAsyncEnumerable<TResponse>> next, CancellationToken ct) =>
        (IAsyncEnumerable<TResponse>)HandleMethod(behavior.GetType()).Invoke(behavior, [request, next, ct]);

    public static Task InvokeNotificationHandler(object handler, object notification, CancellationToken ct) =>
        (Task)HandleMethod(handler.GetType()).Invoke(handler, [notification, ct]);

    public static Task InvokeNotificationBehavior(object behavior, object notification, Func<Task> next, CancellationToken ct) =>
        (Task)HandleMethod(behavior.GetType()).Invoke(behavior, [notification, next, ct]);

    public static Task InvokeNotificationHandlerBehavior(object behavior, object notification, Func<Task> next, CancellationToken ct) =>
        (Task)HandleMethod(behavior.GetType()).Invoke(behavior, [notification, next, ct]);
}
