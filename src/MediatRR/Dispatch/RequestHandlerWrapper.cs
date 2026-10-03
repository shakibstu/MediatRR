using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MediatRR.Dispatch;

internal abstract class RequestHandlerWrapper<TResponse>
{
    public abstract Task<TResponse> Handle(IRequest<TResponse> request, IServiceProvider serviceProvider, CancellationToken cancellationToken);
}

internal sealed class RequestHandlerWrapper<TRequest, TResponse> : RequestHandlerWrapper<TResponse>
    where TRequest : IRequest<TResponse>
{
    public override Task<TResponse> Handle(IRequest<TResponse> request, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var typedRequest = (TRequest)request;
        var behaviors = serviceProvider.GetServices<IPipelineBehavior<TRequest, TResponse>>();

        Task<TResponse> InvokeHandler()
        {
            var handler = serviceProvider.GetService<IRequestHandler<TRequest, TResponse>>()
                ?? throw new InvalidOperationException($"No handler registered for {typeof(TRequest)}.");
            return handler.Handle(typedRequest, cancellationToken);
        }

        var pipeline = behaviors
            .Reverse()
            .Aggregate((Func<Task<TResponse>>)InvokeHandler,
                (next, behavior) => () => behavior.Handle(typedRequest, next, cancellationToken));

        return pipeline();
    }
}
