using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace MediatRR.Dispatch;

internal abstract class StreamRequestHandlerWrapper<TResponse>
{
    public abstract IAsyncEnumerable<TResponse> Handle(IStreamRequest<TResponse> request, IServiceProvider serviceProvider, CancellationToken cancellationToken);
}

internal sealed class StreamRequestHandlerWrapper<TRequest, TResponse> : StreamRequestHandlerWrapper<TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    public override IAsyncEnumerable<TResponse> Handle(IStreamRequest<TResponse> request, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var typedRequest = (TRequest)request;
        var behaviors = serviceProvider.GetServices<IStreamBehavior<TRequest, TResponse>>();

        IAsyncEnumerable<TResponse> InvokeHandler()
        {
            var handler = serviceProvider.GetService<IStreamRequestHandler<TRequest, TResponse>>()
                ?? throw new InvalidOperationException($"No stream handler registered for {typeof(TRequest)}.");
            return handler.Handle(typedRequest, cancellationToken);
        }

        var pipeline = behaviors
            .Reverse()
            .Aggregate((Func<IAsyncEnumerable<TResponse>>)InvokeHandler,
                (next, behavior) => () => behavior.Handle(typedRequest, next, cancellationToken));

        return pipeline();
    }
}
