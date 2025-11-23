using System;
using System.Threading;
using System.Threading.Tasks;

namespace MediatRR.Contract.Messaging
{

    /// <summary>
    /// Pipeline behavior to surround the inner handler.
    /// Implementations add additional behavior and await the next delegate.
    /// </summary>
    /// <typeparam name="TRequest">Request type</typeparam>
    /// <typeparam name="TResponse">Response type</typeparam>
    public interface IPipelineBehavior<in TRequest, TResponse>
    {
        /// <summary>
        /// Pipeline handler. Perform any additional behavior and await the <paramref name="next"/> delegate as necessary
        /// </summary>
        /// <param name="request">Incoming request</param>
        /// <param name="next">Awaitable delegate for the next action in the pipeline. Eventually this delegate represents the handler.</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Awaitable task returning the <typeparamref name="TResponse"/></returns>
        Task<TResponse> Handle(TRequest request, Func<Task<TResponse>> next, CancellationToken cancellationToken);
    }

    public interface INotificationBehavior<in TNotification>
    {
        /// <summary>
        /// Pipeline handler. Perform any additional behavior and await the <paramref name="next"/> delegate as necessary
        /// </summary>
        /// <param name="request">Incoming request</param>
        /// <param name="next">Awaitable delegate for the next action in the pipeline. Eventually this delegate represents the handler.</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task Handle(TNotification request, Func<Task> next, CancellationToken cancellationToken = default);
    }


    public interface INotificationHandlerBehavior<in TNotification>
    {
        /// <summary>
        /// Pipeline handler. Perform any additional behavior and await the <paramref name="next"/> delegate as necessary
        /// </summary>
        /// <param name="request">Incoming request</param>
        /// <param name="next">Awaitable delegate for the next action in the pipeline. Eventually this delegate represents the handler.</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task Handle(TNotification request, Func<Task> next, CancellationToken cancellationToken = default);
    }
}