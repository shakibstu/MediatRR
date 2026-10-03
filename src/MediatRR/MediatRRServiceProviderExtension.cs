using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;
using System.Collections.Concurrent;
using Void = MediatRR.Contract.Messaging.Void;

namespace MediatRR;

/// <summary>
/// Extension methods for registering MediatRR services with the dependency injection container.
/// </summary>
public static class MediatRRServiceProviderExtension
{
    public static IServiceCollection AddMediatRR(this IServiceCollection services, Action<MediatRRConfiguration> configuration, ConcurrentQueue<DeadLettersInfo> deadLetters)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (deadLetters is null) throw new ArgumentNullException(nameof(deadLetters));

        var serviceConfig = new MediatRRConfiguration();
        configuration?.Invoke(serviceConfig);
        Validate(serviceConfig);

        services.TryAddSingleton(new InternalDeadLettersKeeper(deadLetters));
        services.TryAddSingleton(serviceConfig);
        services.TryAddTransient<IMediator, Mediator>();
        services.TryAddSingleton<NotificationChannel>();
        services.TryAddSingleton<NotificationResiliencyProvider>();
        services.AddHostedService<HandleNotificationsWorker>();
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
        if (notificationRetryPolicy is null) return services;

        if (notificationRetryPolicy.MaxRetryAttempts < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(notificationRetryPolicy), notificationRetryPolicy.MaxRetryAttempts, "MaxRetryAttempts cannot be negative.");
        }

        if (notificationRetryPolicy.DelayBetweenRetries < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(notificationRetryPolicy), notificationRetryPolicy.DelayBetweenRetries, "DelayBetweenRetries cannot be negative.");
        }

        services.AddSingleton(new NotificationPolicyRegistration(typeof(T), notificationRetryPolicy));
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

    private static void Validate(MediatRRConfiguration configuration)
    {
        if (configuration.NotificationChannelSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MediatRRConfiguration.NotificationChannelSize), configuration.NotificationChannelSize, "NotificationChannelSize must be at least 1.");
        }

        if (configuration.MaxConcurrentMessageConsumer < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MediatRRConfiguration.MaxConcurrentMessageConsumer), configuration.MaxConcurrentMessageConsumer, "MaxConcurrentMessageConsumer must be at least 1.");
        }
    }
}
