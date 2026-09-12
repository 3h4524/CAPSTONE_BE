using System.Reflection;
using APCS.Application.Features.Auth;
using APCS.Application.Features.Profile;
using APCS.Application.Features.SupportTickets;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace APCS.Application;

/// <summary>
/// Registers application-layer services.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Adds application services and validators.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddValidatorsFromAssembly(assembly);
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<ISupportTicketService, SupportTicketService>();

        return services;
    }
}
