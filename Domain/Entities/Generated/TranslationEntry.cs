using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class TranslationEntry
{
    public Guid Id { get; set; }

    public string SourceHash { get; set; } = null!;

    public string SourceText { get; set; } = null!;

    public string SourceLocale { get; set; } = null!;

    public string TargetLocale { get; set; } = null!;

    public string TranslatedText { get; set; } = null!;

    public string Provider { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
