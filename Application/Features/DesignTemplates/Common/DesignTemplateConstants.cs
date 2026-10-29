namespace APCS.Application.Features.DesignTemplates.Common;

/// <summary>Provides persisted design-template origin values.</summary>
public static class DesignTemplateTypes
{
    public const string System = "system";
    public const string Personal = "personal";

    public static IReadOnlyCollection<string> All { get; } = [System, Personal];
}

/// <summary>Provides the supported design art styles.</summary>
public static class DesignTemplateArtStyles
{
    public const string Vintage = "vintage";
    public const string Minimalist = "minimalist";
    public const string Watercolor = "watercolor";
    public const string BoldTypography = "bold_typography";
    public const string DarkAcademia = "dark_academia";
    public const string FunnyQuote = "funny_quote";
    public const string Floral = "floral";

    public static IReadOnlyDictionary<string, string> Labels { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Vintage] = "Vintage",
            [Minimalist] = "Minimalist",
            [Watercolor] = "Watercolor",
            [BoldTypography] = "Bold typography",
            [DarkAcademia] = "Dark academia",
            [FunnyQuote] = "Funny quote",
            [Floral] = "Floral"
        };

    public static IReadOnlyCollection<string> All { get; } = Labels.Keys.ToArray();
}

/// <summary>Provides the supported template niches.</summary>
public static class DesignTemplateNiches
{
    public const string NatureBotanical = "Nature & Botanical";
    public const string Outdoors = "Outdoors";
    public const string FoodDrink = "Food & Drink";
    public const string Vehicles = "Vehicles";
    public const string QuotesMotivation = "Quotes & Motivation";
    public const string HumorLifestyle = "Humor & Lifestyle";
    public const string GiftsOccasions = "Gifts & Occasions";
    public const string PetsAnimals = "Pets & Animals";
    public const string SportsFitness = "Sports & Fitness";
    public const string TravelPlaces = "Travel & Places";

    public static IReadOnlyCollection<string> All { get; } =
    [
        NatureBotanical,
        Outdoors,
        FoodDrink,
        Vehicles,
        QuotesMotivation,
        HumorLifestyle,
        GiftsOccasions,
        PetsAnimals,
        SportsFitness,
        TravelPlaces
    ];

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> DefaultKeywords { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [NatureBotanical] = ["organic leaves", "balanced botanicals"],
            [Outdoors] = ["mountains", "pines", "open sky"],
            [FoodDrink] = ["simple ingredients", "kitchen motifs"],
            [Vehicles] = ["mechanical details", "heritage parts"],
            [QuotesMotivation] = ["directional marks", "confident shapes"],
            [HumorLifestyle] = ["deadpan character", "simple everyday object"],
            [GiftsOccasions] = ["keepsake details", "balanced ornament"],
            [PetsAnimals] = ["friendly animal details", "simple framing"],
            [SportsFitness] = ["motion marks", "athletic symbols"],
            [TravelPlaces] = ["landmark details", "map-inspired accents"]
        };
}

/// <summary>Provides design-template validation and cache limits.</summary>
public static class DesignTemplateRules
{
    public const int MaximumNameLength = 255;
    public const int MaximumBasePromptLength = 1000;
    public const int MaximumExamples = 5;
    public const int DefaultPageSize = 12;
    public const int MaximumPageSize = 48;
    public const int MaximumCloneWriteAttempts = 3;
    public static readonly TimeSpan SystemCacheExpiration = TimeSpan.FromMinutes(60);
    public static readonly TimeSpan PersonalCacheExpiration = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan CacheOperationTimeout = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan CacheInvalidationTimeout = TimeSpan.FromSeconds(2);
}

/// <summary>Provides the supported prompt placeholders.</summary>
public static class DesignTemplatePlaceholders
{
    public const string Subject = "subject";
    public const string Niche = "niche";
    public const string Style = "style";
    public const string Keywords = "keywords";

    public static IReadOnlyCollection<string> All { get; } = [Subject, Niche, Style, Keywords];
}

/// <summary>Provides design-template list scopes.</summary>
public static class DesignTemplateScopes
{
    public const string All = "all";
    public const string System = "system";
    public const string Personal = "personal";

    public static IReadOnlyCollection<string> Values { get; } = [All, System, Personal];
}

/// <summary>Provides batch-job states that retain a live template dependency.</summary>
public static class ActiveDesignTemplateBatchStatuses
{
    public static IReadOnlyCollection<string> All { get; } =
        ["draft", "validating", "ready", "queued", "running", "paused", "cancelling"];
}

/// <summary>Provides versioned Redis cache keys for the template library.</summary>
internal static class DesignTemplateCacheKeys
{
    public const string SystemCatalog = "design-templates:v1:system:all";

    public static string PersonalCatalog(Guid userId) => $"design-templates:v1:user:{userId:N}:all";
}
