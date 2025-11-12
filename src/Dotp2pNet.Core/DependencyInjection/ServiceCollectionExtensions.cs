using Microsoft.Extensions.DependencyInjection;

namespace Dotp2pNet.Core.DependencyInjection;

/// <summary>
/// Extension methods for configuring Dotp2pNet services in the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds core Dotp2pNet services to the service collection.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddDotp2pNetCore(this IServiceCollection services)
    {
        // Core services will be registered here as they are implemented
        // Example: services.AddSingleton<IMessageFramer, MessageFramer>();
        
        return services;
    }
}
