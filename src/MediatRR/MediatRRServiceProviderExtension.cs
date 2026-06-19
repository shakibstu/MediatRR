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
        public static IServiceCollection AddMediatRR(this IServiceCollection services, Action<MediatRRConfiguration> configuration, ConcurrentQueue<DeadLettersInfo> deadLetters)
        {
            if (services is null) throw new ArgumentNullException(nameof(services));
            if (deadLetters is null) throw new ArgumentNullException(nameof(deadLetters));

            var serviceConfig = new MediatRRConfiguration { NotificationChannelSize = 10_000, MaxConcurrentMessageConsumer = 5 };
            configuration?.Invoke(serviceConfig);

            services.AddSingleton(new InternalDeadLettersKeeper(deadLetters));
            return services.AddMediatRR(serviceConfig);
        }

        public static IServiceCollection AddMediatRR(this IServiceCollection services, MediatRRConfiguration configuration)
        {
            if (services is null) throw new ArgumentNullException(nameof(services));
            if (configuration is null) throw new ArgumentNullException(nameof(configuration));

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
        public static IServiceCollection AddNotificationHandler<T, THandler>(this IServiceCollection services, NotificationRetryPolicy notificationRetryPolicy = null)
            where THandler : class, INotificationHandler<T> where T : INotification
        {
            if (services is null) throw new ArgumentNullException(nameof(services));

            services.AddTransient<INotificationHandler<T>, THandler>();

            var policy = notificationRetryPolicy ?? NotificationRetryPolicy.Default;
            services.AddSingleton(new NotificationPolicyRegistration(typeof(T), policy));
            return services;
        }

        public static IServiceCollection AddRequestHandler<T, TResponse, THandler>(this IServiceCollection services)
            where THandler : class, IRequestHandler<T, TResponse> where T : IRequest<TResponse>
        {
            if (services is null) throw new ArgumentNullException(nameof(services));
            services.AddTransient<IRequestHandler<T, TResponse>, THandler>();
            return services;
        }

        public static IServiceCollection AddStreamRequestHandler<T, TResponse, THandler>(this IServiceCollection services)
            where THandler : class, IStreamRequestHandler<T, TResponse> where T : IStreamRequest<TResponse>
        {
            if (services is null) throw new ArgumentNullException(nameof(services));
            services.AddTransient<IStreamRequestHandler<T, TResponse>, THandler>();
            return services;
        }

        public static IServiceCollection AddRequestHandler<T, THandler>(this IServiceCollection services)
            where THandler : class, IRequestHandler<T, Void> where T : IRequest<Void>
        {
            return services.AddRequestHandler<T, Void, THandler>();
        }
    }
}
