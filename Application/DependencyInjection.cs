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
using APCS.Application.Features.Commerce;
using APCS.Application.Abstractions.Commerce;
using APCS.Application.Features.Rag;
using APCS.Application.Features.Translations;
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
        services.AddScoped<Features.SellerDashboard.ISellerDashboardService, Features.SellerDashboard.SellerDashboardService>();
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
        // One ShopService per request, reached both as IShopService and directly by publishing, which reuses its token handling.
        services.AddScoped<Features.Shops.ShopService>();
        services.AddScoped<Features.Shops.IShopService>(provider => provider.GetRequiredService<Features.Shops.ShopService>());
        services.AddScoped<Features.Shops.IEtsyPublishingService, Features.Shops.EtsyPublishingService>();
        services.AddScoped<Features.Shops.IPrintifyService, Features.Shops.PrintifyService>();
        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<ISupportTicketService, SupportTicketService>();
        services.AddScoped<IAdminDashboardService, AdminDashboardService>();
        services.AddScoped<IAdminOperationsService, AdminOperationsService>();
        services.AddScoped<IBatchService, BatchService>();
        services.AddScoped<IDesignTemplateService, DesignTemplateService>();
        services.AddScoped<IAdminSubscriptionPlanService, AdminSubscriptionPlanService>();
        services.AddScoped<IStyleArtPresetService, StyleArtPresetService>();
        services.AddScoped<IBatchProductPromptService, BatchProductPromptService>();
        services.AddScoped<IMockupTemplateService, MockupTemplateService>();
        services.AddScoped<IModelPhotoService, ModelPhotoService>();
        services.AddScoped<IModelPhotoGenerationService, ModelPhotoGenerationService>();
        services.AddScoped<IAdminUserService, AdminUserService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IRevenueService, RevenueService>();
        services.AddScoped<ISystemStatisticsService, SystemStatisticsService>();
        services.AddScoped<Features.ImageQuality.IImageQualityService, Features.ImageQuality.ImageQualityService>();
        services.AddScoped<IDesignGenerationService, DesignGenerationService>();
        services.AddSingleton<Features.Workflows.WorkflowCapabilityRegistry>();
        services.AddScoped<Features.Workflows.WorkflowGraphValidator>();
        services.AddScoped<Features.Workflows.WorkflowPublishConnections>();
        services.AddScoped<Features.Workflows.VideoStoryboardPlanner>();
        services.AddScoped<Features.Workflows.IVideoPromptPreparationService, Features.Workflows.VideoPromptPreparationService>();
        services.AddScoped<Features.Workflows.IVideoExperienceContextService, Features.Workflows.VideoExperienceContextService>();
        services.AddScoped<Features.Workflows.WorkflowRuntime>();
        services.AddScoped<Features.Workflows.IWorkflowPublishService, Features.Workflows.WorkflowPublishService>();
        services.AddScoped<Features.Workflows.IWorkflowService, Features.Workflows.WorkflowService>();
        services.AddScoped<Features.Workflows.IWorkflowRunService, Features.Workflows.WorkflowRunService>();
        services.AddScoped<Features.Workflows.IWorkflowRunReconciler, Features.Workflows.WorkflowRunReconciler>();
        services.AddScoped<Features.Workflows.IMediaLeaseRecovery, Features.Workflows.MediaLeaseRecovery>();
        services.AddScoped<Features.Workflows.IRunTargetConfigResolver, Features.Workflows.RunTargetConfigResolver>();
        services.AddScoped<Features.Workflows.IWorkflowRunGroupService, Features.Workflows.WorkflowRunGroupService>();
        services.AddScoped<Features.Workflows.IRunCostEstimateService, Features.Workflows.RunCostEstimateService>();
        services.AddScoped<Features.Workflows.IRunGroupExportService, Features.Workflows.RunGroupExportService>();
        services.AddScoped<Features.Workflows.IRunGroupPlanner, Features.Workflows.RunGroupPlanner>();
        services.AddScoped<Features.Workflows.IMockupAssetService, Features.Workflows.MockupAssetService>();
        services.AddScoped<Features.Workflows.IGeneratedMockupSourceService, Features.Workflows.GeneratedMockupSourceService>();
        services.AddScoped<Features.Workflows.ILifestyleMockupService, Features.Workflows.LifestyleMockupService>();
        services.AddScoped<Features.Workflows.IModelPhotoMockupService, Features.Workflows.ModelPhotoMockupService>();
        services.AddScoped<Features.Workflows.IAiStudioFrameService, Features.Workflows.AiStudioFrameService>();
        services.AddScoped<Features.Workflows.IMediaJobService, Features.Workflows.MediaJobService>();
        services.AddScoped<Features.Workflows.IVideoArtifactService, Features.Workflows.VideoArtifactService>();
        services.AddScoped<Features.Workflows.IBatchExportPreviewService, Features.Workflows.BatchExportPreviewService>();
        services.AddScoped<Features.Workflows.IVideoQualityReviewService, Features.Workflows.VideoQualityReviewService>();
        services.AddScoped<Features.Workflows.IVideoQualityJobService, Features.Workflows.VideoQualityJobService>();
        services.AddScoped<Features.Workflows.IVideoPlanningService, Features.Workflows.VideoPlanningService>();
        services.AddScoped<Features.Workflows.IAiShotQuoteService, Features.Workflows.AiShotQuoteService>();
        services.AddScoped<Features.Workflows.IAiShotJobService, Features.Workflows.AiShotJobService>();
        services.AddScoped<APCS.Application.Abstractions.AI.IAiVideoGenerationStrategyFactory, Features.Workflows.AiVideoGenerationStrategyFactory>();
        services.AddScoped<Features.Workflows.Validators.SaveWorkflowValidator>();
        services.AddScoped<Features.Workflows.Validators.GenerateVideoConfigValidator>();
        services.AddScoped<Features.Workflows.Validators.MockupMetadataValidator>();
        services.AddScoped<Features.Assistant.IAssistantService, Features.Assistant.AssistantService>();
        services.AddScoped<Features.Assistant.AssistantAgentLoop>();
        services.AddScoped<Features.Assistant.AssistantKnowledgeBase>();
        services.AddScoped<Features.Assistant.AssistantPromptBuilder>();
        services.AddScoped<Features.Assistant.AssistantToolRegistry>();
        services.AddScoped<Features.Assistant.Tools.IAssistantTool, Features.Assistant.Tools.Query.SearchFaqTool>();
        services.AddScoped<Features.Assistant.Tools.IAssistantTool, Features.Assistant.Tools.Query.ListWorkflowsTool>();
        services.AddScoped<Features.Assistant.Tools.IAssistantTool, Features.Assistant.Tools.Query.GetWorkflowTool>();
        services.AddScoped<Features.Assistant.Tools.IAssistantTool, Features.Assistant.Tools.Query.GetWorkflowRunTool>();
        services.AddScoped<Features.Assistant.Tools.IAssistantTool, Features.Assistant.Tools.Query.GetWorkflowCapabilitiesTool>();
        services.AddScoped<Features.Assistant.Tools.IAssistantTool, Features.Assistant.Tools.Query.ListBatchesTool>();
        services.AddScoped<Features.Assistant.Tools.IAssistantTool, Features.Assistant.Tools.Query.ListBatchProductsTool>();
        services.AddScoped<Features.Assistant.Tools.IAssistantTool, Features.Assistant.Tools.Query.GetUsageOverviewTool>();
        services.AddScoped<Features.Assistant.Tools.IAssistantTool, Features.Assistant.Tools.Query.GetSubscriptionOverviewTool>();
        services.AddScoped<Features.Assistant.Tools.IAssistantTool, Features.Assistant.Tools.Query.GetNodeCatalogTool>();
        services.AddScoped<Features.Assistant.Tools.IAssistantTool, Features.Assistant.Tools.Query.ListDesignTemplatesTool>();
        services.AddScoped<Features.Assistant.Tools.IAssistantTool, Features.Assistant.Tools.Query.ListStylePresetsTool>();
        services.AddScoped<Features.Assistant.Tools.IAssistantTool, Features.Assistant.Tools.Query.ListMockupTemplatesTool>();
        services.AddScoped<Features.Assistant.Tools.IAssistantTool, Features.Assistant.Tools.Query.ListVideoTemplatesTool>();
        services.AddSingleton<Features.Assistant.Tools.ClientActions.WorkflowCanvasPatchSimulator>();
        services.AddScoped<Features.Assistant.Tools.IAssistantTool, Features.Assistant.Tools.ClientActions.EditWorkflowCanvasTool>();
        services.AddScoped<Features.Assistant.Tools.IAssistantTool, Features.Assistant.Tools.ClientActions.NavigateTool>();
        services.AddScoped<Features.Assistant.Tools.IAssistantTool, Features.Assistant.Tools.ClientActions.ArrangeWorkflowCanvasTool>();
        services.AddScoped<Features.Assistant.Tools.IAssistantTool, Features.Assistant.Tools.Confirmations.StartWorkflowRunTool>();
        services.AddScoped<Features.Assistant.Tools.IAssistantTool, Features.Assistant.Tools.Confirmations.WorkflowRunActionTool>();
        services.AddScoped<Features.Assistant.Tools.IAssistantTool, Features.Assistant.Tools.OfferChoicesTool>();
        services.AddScoped<IListingContentService, ListingContentService>();
        // The test reports are files on the machine that ran the suite, so the service keeps
        // them for as long as the API lives.
        services.AddSingleton<ITestReportService, TestReportService>();
        services.AddScoped<ICommerceConnectorFactory, CommerceConnectorFactory>();
        services.AddScoped<ICommerceMarketDataConnectorFactory, CommerceMarketDataConnectorFactory>();
        services.AddScoped<IMarketResearchService, MarketResearchService>();
        services.AddScoped<IMarketInsightsService, MarketInsightsService>();
        services.AddScoped<IOpportunityService, OpportunityService>();
        services.AddScoped<IOpportunityAnalysisService, OpportunityAnalysisService>();
        services.AddScoped<ITranslationService, TranslationService>();
        services.AddScoped<IRagBenchmarkService, RagBenchmarkService>();

        return services;
    }
}
