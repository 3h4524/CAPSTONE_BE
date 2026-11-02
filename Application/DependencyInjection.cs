using System.Reflection;
using APCS.Application.Features.Admin;
using APCS.Application.Features.Admin.SubscriptionPlans;
using APCS.Application.Features.Auth;
using APCS.Application.Features.ApiKeys;
using APCS.Application.Features.BatchProductPrompts;
using APCS.Application.Features.BatchMockups;
using APCS.Application.Features.Profile;
using APCS.Application.Features.Revenues;
using APCS.Application.Features.StyleArtPresets;
using APCS.Application.Features.Subscriptions;
using APCS.Application.Features.UsageStatistics;
using APCS.Application.Features.SupportTickets;
using APCS.Application.Features.TestReports;
using APCS.Application.Features.Batches;
using APCS.Application.Features.DesignGeneration;
using APCS.Application.Features.DesignTemplates;
using APCS.Application.Abstractions;
using APCS.Application.Features.Notifications;
using APCS.Application.Features.SystemStatistics;
using APCS.Application.Features.ListingContent;
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
        services.AddSingleton<Features.Entitlements.FeatureCatalogCache>();
        services.AddSingleton<Features.Entitlements.PlanSettingsCache>();
        services.AddScoped<Features.Entitlements.IPlanSettings, Features.Entitlements.PlanSettings>();
        services.AddScoped<Features.Admin.SubscriptionPlans.IPlanSettingsAdminService, Features.Admin.SubscriptionPlans.PlanSettingsAdminService>();
        services.AddScoped<Features.Admin.SubscriptionPlans.IPlanToolsService, Features.Admin.SubscriptionPlans.PlanToolsService>();
        services.AddScoped<Features.Admin.SubscriptionPlans.IPlanChangeLog, Features.Admin.SubscriptionPlans.PlanChangeLog>();
        services.AddScoped<Features.Entitlements.IFeatureCatalog, Features.Entitlements.FeatureCatalog>();
        services.AddScoped<Features.Admin.SubscriptionPlans.IFeatureCatalogAdminService, Features.Admin.SubscriptionPlans.FeatureCatalogAdminService>();
        services.AddScoped<Features.Admin.SubscriptionPlans.IPlanFeatureAssignmentService, Features.Admin.SubscriptionPlans.PlanFeatureAssignmentService>();
        services.AddScoped<Features.Entitlements.IPlanEntitlementService, Features.Entitlements.PlanEntitlementService>();
        services.AddScoped<Features.Entitlements.IUsageAllowanceService, Features.Entitlements.UsageAllowanceService>();
        services.AddScoped<Features.Admin.SubscriptionPlans.IPlanInsightsService, Features.Admin.SubscriptionPlans.PlanInsightsService>();
        services.AddScoped<Features.Admin.SubscriptionPlans.AiReview.IPlanAiReviewService, Features.Admin.SubscriptionPlans.AiReview.PlanAiReviewService>();
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
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IRevenueService, RevenueService>();
        services.AddScoped<ISystemStatisticsService, SystemStatisticsService>();
        services.AddScoped<IDesignGenerationService, DesignGenerationService>();
        services.AddSingleton<Features.Workflows.WorkflowCapabilityRegistry>();
        services.AddScoped<Features.Workflows.WorkflowGraphValidator>();
        services.AddScoped<Features.Workflows.VideoStoryboardPlanner>();
        services.AddScoped<Features.Workflows.WorkflowRuntime>();
        services.AddScoped<Features.Workflows.IWorkflowService, Features.Workflows.WorkflowService>();
        services.AddScoped<Features.Workflows.IWorkflowRunService, Features.Workflows.WorkflowRunService>();
        services.AddScoped<Features.Workflows.IMockupAssetService, Features.Workflows.MockupAssetService>();
        services.AddScoped<Features.Workflows.IGeneratedMockupSourceService, Features.Workflows.GeneratedMockupSourceService>();
        services.AddScoped<Features.Workflows.ILifestyleMockupService, Features.Workflows.LifestyleMockupService>();
        services.AddScoped<Features.Workflows.IAiStudioFrameService, Features.Workflows.AiStudioFrameService>();
        services.AddScoped<Features.Workflows.IMediaJobService, Features.Workflows.MediaJobService>();
        services.AddScoped<Features.Workflows.IVideoArtifactService, Features.Workflows.VideoArtifactService>();
        services.AddScoped<Features.Workflows.IVideoPlanningService, Features.Workflows.VideoPlanningService>();
        services.AddScoped<Features.Workflows.IAiShotQuoteService, Features.Workflows.AiShotQuoteService>();
        services.AddScoped<Features.Workflows.IAiShotJobService, Features.Workflows.AiShotJobService>();
        services.AddScoped<APCS.Application.Abstractions.AI.IAiVideoGenerationStrategyFactory, Features.Workflows.AiVideoGenerationStrategyFactory>();
        services.AddScoped<Features.Workflows.Validators.SaveWorkflowValidator>();
        services.AddScoped<Features.Workflows.Validators.GenerateVideoConfigValidator>();
        services.AddScoped<Features.Workflows.Validators.MockupMetadataValidator>();
        services.AddScoped<IListingContentService, ListingContentService>();
        // The test reports are files on the machine that ran the suite, so the service keeps
        // them for as long as the API lives.
        services.AddSingleton<ITestReportService, TestReportService>();

        return services;
    }
}
