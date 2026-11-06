using APCS.Domain.Entities;

namespace APCS.Application.Features.BatchMockups.Common;

/// <summary>Who is in a model photo, for filtering the library and for making one with AI.</summary>
/// <param name="People">'single' for one person, 'group' for several, all dressed in the same mock-up.</param>
/// <param name="AgeGroup">For one person: 'child', 'teen', 'adult' or 'senior'.</param>
/// <param name="Gender">For one person: 'female' or 'male'.</param>
/// <param name="GroupType">For a group: 'family', 'friends' or 'couple'.</param>
public sealed record ModelAttributesDto(string People, string? AgeGroup = null, string? Gender = null, string? GroupType = null);

/// <summary>Narrows the model photos listed; a null part matches any.</summary>
public sealed record ModelPhotoFilter(string? People = null, string? AgeGroup = null, string? Gender = null, string? GroupType = null)
{
    public static readonly ModelPhotoFilter Any = new();
}

/// <summary>The values a model photo's attributes take, and how they are checked and kept.</summary>
public static class ModelPeople
{
    public const string Single = "single";
    public const string Group = "group";

    public static readonly IReadOnlyList<string> PeopleValues = [Single, Group];
    public static readonly IReadOnlyList<string> AgeGroups = ["child", "teen", "adult", "senior"];
    public static readonly IReadOnlyList<string> Genders = ["female", "male"];
    public static readonly IReadOnlyList<string> GroupTypes = ["family", "friends", "couple"];

    /// <summary>
    /// What is wrong with a set of attributes, or null: one person has an age group and a gender and no group type;
    /// a group has a group type and neither of the others.
    /// </summary>
    public static string? Problem(ModelAttributesDto attributes)
    {
        var people = Normalize(attributes.People);
        if (people is null || !PeopleValues.Contains(people))
            return "Choose one person or several people.";
        if (people == Single)
        {
            if (Normalize(attributes.AgeGroup) is not { } age || !AgeGroups.Contains(age))
                return "Choose the person's age group.";
            if (Normalize(attributes.Gender) is not { } gender || !Genders.Contains(gender))
                return "Choose the person's gender.";
            return Normalize(attributes.GroupType) is null ? null : "A photo of one person has no group type.";
        }

        if (Normalize(attributes.GroupType) is not { } group || !GroupTypes.Contains(group))
            return "Choose who the group is: a family, friends or a couple.";
        return Normalize(attributes.AgeGroup) is null && Normalize(attributes.Gender) is null
            ? null
            : "A photo of several people has no single age group or gender.";
    }

    /// <summary>A template's attributes, or null when it has none.</summary>
    public static ModelAttributesDto? Of(MockupTemplate template) =>
        template.People is null ? null : new ModelAttributesDto(template.People, template.AgeGroup, template.Gender, template.GroupType);

    /// <summary>Puts checked attributes on a template, lower-cased, leaving out the parts that don't apply.</summary>
    public static void Apply(MockupTemplate template, ModelAttributesDto attributes)
    {
        var people = Normalize(attributes.People)!;
        template.People = people;
        template.AgeGroup = people == Single ? Normalize(attributes.AgeGroup) : null;
        template.Gender = people == Single ? Normalize(attributes.Gender) : null;
        template.GroupType = people == Group ? Normalize(attributes.GroupType) : null;
    }

    /// <summary>A filter with its values lower-cased and blanks taken as "any".</summary>
    public static ModelPhotoFilter Clean(ModelPhotoFilter filter) => new(
        Normalize(filter.People), Normalize(filter.AgeGroup), Normalize(filter.Gender), Normalize(filter.GroupType));

    /// <summary>A short label for a photo with these attributes, e.g. "Adult · Female" or "Family".</summary>
    public static string Label(ModelAttributesDto attributes)
    {
        static string Title(string? value) => string.IsNullOrEmpty(value) ? "" : char.ToUpperInvariant(value[0]) + value[1..];
        return Normalize(attributes.People) == Group
            ? Title(Normalize(attributes.GroupType))
            : $"{Title(Normalize(attributes.AgeGroup))} · {Title(Normalize(attributes.Gender))}";
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}
