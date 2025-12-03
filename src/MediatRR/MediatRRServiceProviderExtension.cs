using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Concurrent;
using Void = MediatRR.Contract.Messaging.Void;

namespace MediatRR
{
    /// <summary>
    /// Extension methods for registering MediatRR services with the dependency injection container.
    /// </summary>
    public static class MediatRRServiceProviderExtension
    {
        /// <summary>
        /// Registers MediatRR services with the service collection using a configuration action and dead letter queue.
        /// </summary>
        /// <param name="services">The service collection to add services to</param>
        /// <param name="configuration">Action to configure MediatRR options</param>
        /// <param name="deadLetters">Queue to store failed notifications that exceeded retry attempts</param>
        /// <returns>The service collection for method chaining</returns>
        public static IServiceCollection AddMediatRR(this IServiceCollection services, Action<MediatRRConfiguration> configuration, ConcurrentQueue<DeadLettersInfo> deadLetters)
        {
            // Initialize with default configuration values
            var serviceConfig = new MediatRRConfiguration { NotificationChannelSize = 10_000, MaxConcurrentMessageConsumer = 5 };

            // Apply user configuration
            configuration?.Invoke(serviceConfig);

            // Register the dead letter queue
            services.AddSingleton(new InternalDeadLettersKeeper { DeadLettersQueue = deadLetters });

            return services.AddMediatRR(serviceConfig);
        }

        /// <summary>
        /// Registers core MediatRR services with the service collection.
        /// </summary>
        /// <param name="services">The service collection to add services to</param>
        /// <param name="configuration">MediatRR configuration options</param>
        /// <returns>The service collection for method chaining</returns>
        public static IServiceCollection AddMediatRR(this IServiceCollection services, MediatRRConfiguration configuration)
        {
            services.AddTransient<IMediator, Mediator>();
            services.AddSingleton<NotificationChannel>();
            services.AddSingleton<IHostedService, HandleNotificationsWorker>();
            services.AddSingleton(configuration);
            services.AddSingleton<NotificationResiliencyProvider>();
            return services;
        }

        /// <summary>
        /// Registers a notification handler with an optional retry policy.
        /// </summary>
        /// <typeparam name="T">The notification type</typeparam>
        /// <typeparam name="THandler">The handler implementation type</typeparam>
        /// <param name="services">The service collection to add services to</param>
        /// <param name="notificationRetryPolicy">Retry policy for this notification type. If null, uses default policy.</param>
        /// <returns>The service collection for method chaining</returns>
        public static IServiceCollection AddNotificationHandler<T, THandler>(this IServiceCollection services, NotificationRetryPolicy notificationRetryPolicy)
            where THandler : class, INotificationHandler<T> where T : INotification
        {
            // Register the handler
            services.AddTransient<INotificationHandler<T>>(a =>
            {
                var resiliencies = a.GetRequiredService<NotificationResiliencyProvider>();
                resiliencies.SetResiliencyPolicy(typeof(T), notificationRetryPolicy ?? new NotificationRetryPolicy());
                return ActivatorUtilities.CreateInstance<THandler>(a);
            });

            return services;
        }

        /// <summary>
        /// Registers a request handler that returns a response.
        /// </summary>
        /// <typeparam name="T">The request type</typeparam>
        /// <typeparam name="TResponse">The response type</typeparam>
        /// <typeparam name="THandler">The handler implementation type</typeparam>
        /// <param name="services">The service collection to add services to</param>
        /// <returns>The service collection for method chaining</returns>
        public static IServiceCollection AddRequestHandler<T, TResponse, THandler>(this IServiceCollection services)
            where THandler : class, IRequestHandler<T, TResponse> where T : IRequest<TResponse>
        {
            services.AddTransient<IRequestHandler<T, TResponse>, THandler>();
            return services;
        }

        /// <summary>
        /// Registers a request handler that does not return a response (returns Void).
        /// </summary>
        /// <typeparam name="T">The request type</typeparam>
        /// <typeparam name="THandler">The handler implementation type</typeparam>
        /// <param name="services">The service collection to add services to</param>
        /// <returns>The service collection for method chaining</returns>
        public static IServiceCollection AddRequestHandler<T, THandler>(this IServiceCollection services)
            where THandler : class, IRequestHandler<T, Void> where T : IRequest<Void>
        {
            services.AddRequestHandler<T, Void, THandler>();
            return services;
        }


    }
}
