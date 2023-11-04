using Microsoft.Extensions.DependencyInjection;
using Remora.Discord.API.Abstractions.Gateway.Events;
using Remora.Discord.Gateway.Extensions;
using Remora.Extensions.Options.Immutable;
using RemoraDelegateDispatch.Services;

namespace RemoraDelegateDispatch.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the responder service responsible for invoking delegate responders.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The configured service collection.</returns>
    public static IServiceCollection AddDelegateResponders(this IServiceCollection services) 
        => services.AddResponder<DelegateMapResponder>();

    /// <summary>
    /// Adds a delegate-based responder.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="responder">The responder to add.</param>
    /// <typeparam name="TEvent">The event the delegate will respond to.</typeparam>
    /// <returns>The configured service collection.</returns>
    /// <remarks>The type-safety of the delegate is validated during the initial build of the service provider.</remarks>
    public static IServiceCollection AddDelegateResponder<TEvent>(this IServiceCollection services, Delegate responder) 
        where TEvent : IGatewayEvent => services.Configure<DelegateMapBuilder>(mb => mb.AddResponderAsync<TEvent>(responder));
    
}