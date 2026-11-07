using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class AiCharacterModel
{
    public Guid Id { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string Continent { get; set; } = null!;

    public string Gender { get; set; } = null!;

    public short AgeMin { get; set; }

    public short AgeMax { get; set; }

    public string BodyType { get; set; } = null!;

    public string SkinTone { get; set; } = null!;

    public string Description { get; set; } = null!;

    public string CloudinaryPublicId { get; set; } = null!;

    public string ImageUrl { get; set; } = null!;

    public string PreviewUrl { get; set; } = null!;

    public int SortOrder { get; set; }

    public bool IsDefault { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }
}
