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

    /// <summary>
    /// Notification behavior to surround the notification publishing process.
    /// Implementations add additional behavior and await the next delegate.
    /// </summary>
    /// <typeparam name="TNotification">Notification type</typeparam>
    public interface INotificationBehavior<in TNotification>
    {
        /// <summary>
        /// Pipeline handler. Perform any additional behavior and await the <paramref name="next"/> delegate as necessary
        /// </summary>
        /// <param name="request">Incoming notification</param>
        /// <param name="next">Awaitable delegate for the next action in the pipeline. Eventually this delegate represents the handler.</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task Handle(TNotification request, Func<Task> next, CancellationToken cancellationToken = default);
    }


    /// <summary>
    /// Notification handler behavior to surround individual notification handler execution.
    /// Implementations add additional behavior and await the next delegate.
    /// </summary>
    /// <typeparam name="TNotification">Notification type</typeparam>
    public interface INotificationHandlerBehavior<in TNotification>
    {
        /// <summary>
        /// Pipeline handler. Perform any additional behavior and await the <paramref name="next"/> delegate as necessary
        /// </summary>
        /// <param name="request">Incoming notification</param>
        /// <param name="next">Awaitable delegate for the next action in the pipeline. Eventually this delegate represents the handler.</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task Handle(TNotification request, Func<Task> next, CancellationToken cancellationToken = default);
    }
}