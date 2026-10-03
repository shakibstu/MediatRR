using System;
using System.Collections.Concurrent;

namespace MediatRR.Dispatch;

internal static class WrapperCache
{
    private static readonly ConcurrentDictionary<(Type Request, Type Response), object> RequestWrappers = new();
    private static readonly ConcurrentDictionary<(Type Request, Type Response), object> StreamWrappers = new();
    private static readonly ConcurrentDictionary<Type, NotificationWrapper> NotificationWrappers = new();

    public static RequestHandlerWrapper<TResponse> ForRequest<TResponse>(Type requestType) =>
        (RequestHandlerWrapper<TResponse>)RequestWrappers.GetOrAdd((requestType, typeof(TResponse)),
            static key => Activator.CreateInstance(typeof(RequestHandlerWrapper<,>).MakeGenericType(key.Request, key.Response)));

    public static StreamRequestHandlerWrapper<TResponse> ForStream<TResponse>(Type requestType) =>
        (StreamRequestHandlerWrapper<TResponse>)StreamWrappers.GetOrAdd((requestType, typeof(TResponse)),
            static key => Activator.CreateInstance(typeof(StreamRequestHandlerWrapper<,>).MakeGenericType(key.Request, key.Response)));

    public static NotificationWrapper ForNotification(Type notificationType) =>
        NotificationWrappers.GetOrAdd(notificationType,
            static type => (NotificationWrapper)Activator.CreateInstance(typeof(NotificationWrapper<>).MakeGenericType(type)));
}
