using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Xml;
using Void = MediatRR.Contract.Messaging.Void;

namespace MediatRR
{
    public static class MediatRRServiceProviderExtension
    {    
        /// <summary>
        /// Registers handlers and mediator types from the specified assemblies
        /// </summary>
        /// <param name="services">Service collection</param>
        /// <param name="configuration">The action used to configure the options</param>
        /// <returns>Service collection</returns>
        public static IServiceCollection AddMediatRR(this IServiceCollection services,
            Action<MediatRRConfiguration> configuration)
        {
            var serviceConfig = new MediatRRConfiguration { NotificationChannelSize = 10_000 };

            configuration.Invoke(serviceConfig);

            return services.AddMediatRR(serviceConfig);
        }

        /// <summary>
        /// Registers handlers and mediator types from the specified assemblies
        /// </summary>
        /// <param name="services">Service collection</param>
        /// <param name="configuration">Configuration options</param>
        /// <returns>Service collection</returns>
        public static IServiceCollection AddMediatRR(this IServiceCollection services,
            MediatRRConfiguration configuration)
        {
            services.AddTransient<IMediator, Mediator>();
            services.AddSingleton<NotificationChannel>();
            services.AddSingleton<IHostedService, HandleNotificationsWorker>();
            services.AddSingleton(configuration);
            return services;
        }

        public static IServiceCollection AddNotificationHandler<T,THandler>(this IServiceCollection services, NotificationRetryPolicy notificationRetryPolicy)
            where THandler: class, INotificationHandler<T> where T : INotification
        {
            services.AddTransient<INotificationHandler<T>, THandler>();
            ConcurrentDictionary<Type, NotificationRetryPolicy> dictionary;

            var descriptor = services.FirstOrDefault(d =>
                d.ServiceType == typeof(ConcurrentDictionary<Type, NotificationRetryPolicy>));

            if (descriptor?.ImplementationInstance is ConcurrentDictionary<Type, NotificationRetryPolicy> existingDict)
            {
                dictionary = existingDict;
            }
            else
            {
                // Create and register if it doesn't exist
                dictionary = new ConcurrentDictionary<Type, NotificationRetryPolicy>();
                services.AddSingleton(dictionary);
            }

            dictionary.TryAdd(typeof(T), notificationRetryPolicy);

            return services;
        }
        public static IServiceCollection AddRequestHandler<T,TResponse, THandler>(this IServiceCollection services)
            where THandler : class, IRequestHandler<T, TResponse> where T : IRequest<TResponse>
        {
            services.AddTransient<IRequestHandler<T,TResponse>, THandler>();
            return services;
        }
        public static IServiceCollection AddRequestHandler<T, THandler>(this IServiceCollection services)
            where THandler : class, IRequestHandler<T, Void> where T : IRequest<Void>
        {
            services.AddRequestHandler<T, Void, THandler>();
            return services;
        }
    }
}
