using System.Reflection;
using APCS.Application.Features.Admin;
using APCS.Application.Features.Admin.SubscriptionPlans;
using APCS.Application.Features.Auth;
using APCS.Application.Features.ApiKeys;
using APCS.Application.Features.BatchProductPrompts;
using APCS.Application.Features.BatchMockups;
using APCS.Application.Features.Profile;
using APCS.Application.Features.StyleArtPresets;
using APCS.Application.Features.Subscriptions;
using APCS.Application.Features.UsageStatistics;
using APCS.Application.Features.SupportTickets;
using APCS.Application.Features.Batches;
using APCS.Application.Features.DesignTemplates;
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
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        services.AddScoped<IUsageStatisticsService, UsageStatisticsService>();
        services.AddScoped<IApiKeyService, ApiKeyService>();
        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<ISupportTicketService, SupportTicketService>();
        services.AddScoped<IAdminDashboardService, AdminDashboardService>();
        services.AddScoped<IBatchService, BatchService>();
        services.AddScoped<IDesignTemplateService, DesignTemplateService>();
        services.AddScoped<IAdminSubscriptionPlanService, AdminSubscriptionPlanService>();
        services.AddScoped<IStyleArtPresetService, StyleArtPresetService>();
        services.AddScoped<IBatchProductPromptService, BatchProductPromptService>();
        services.AddScoped<IMockupTemplateService, MockupTemplateService>();
        services.AddScoped<IAdminUserService, AdminUserService>();
        services.AddSingleton<Features.Workflows.WorkflowCapabilityRegistry>();
        services.AddScoped<Features.Workflows.WorkflowGraphValidator>();
        services.AddScoped<Features.Workflows.VideoStoryboardPlanner>();
        services.AddScoped<Features.Workflows.WorkflowRuntime>();
        services.AddScoped<Features.Workflows.IWorkflowService, Features.Workflows.WorkflowService>();
        services.AddScoped<Features.Workflows.IWorkflowRunService, Features.Workflows.WorkflowRunService>();
        services.AddScoped<Features.Workflows.IMockupAssetService, Features.Workflows.MockupAssetService>();
        services.AddScoped<Features.Workflows.IMediaJobService, Features.Workflows.MediaJobService>();
        services.AddScoped<Features.Workflows.IVideoArtifactService, Features.Workflows.VideoArtifactService>();
        services.AddScoped<Features.Workflows.IVideoPlanningService, Features.Workflows.VideoPlanningService>();
        services.AddScoped<Features.Workflows.Validators.SaveWorkflowValidator>();
        services.AddScoped<Features.Workflows.Validators.GenerateVideoConfigValidator>();
        services.AddScoped<Features.Workflows.Validators.MockupMetadataValidator>();

        return services;
    }
}
