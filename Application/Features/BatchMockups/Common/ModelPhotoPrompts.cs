namespace APCS.Application.Features.BatchMockups.Common;

/// <summary>
/// The prompt a model photo is made from with AI. It is put together only from fixed wording for the chosen
/// attributes, never from free text, so every photo is a plain catalogue shot: people facing the camera in a plain
/// white t-shirt, on a backdrop the shirt stands out from, which is what the no-AI mock-up compositing needs.
/// </summary>
public static class ModelPhotoPrompts
{
    /// <summary>The prompt and the aspect ratio for a photo with these attributes (checked beforehand).</summary>
    public static (string Prompt, string AspectRatio) Build(ModelAttributesDto attributes)
    {
        var group = attributes.People.Trim().ToLowerInvariant() == ModelPeople.Group;
        var (subject, count, mood) = group ? GroupSubject(attributes.GroupType) : (PersonSubject(attributes.AgeGroup, attributes.Gender), 1, "");
        var wearing = group
            ? "Everyone wears the same plain white crew-neck short-sleeve cotton t-shirt with no print, logo, text or pattern, untucked, with dark jeans."
            : "They wear a plain white crew-neck short-sleeve cotton t-shirt with no print, logo, text or pattern, untucked, with dark jeans.";
        // Relaxed and natural, but every t-shirt front stays in view and unhidden: the design is printed there, and on
        // a group each person's shirt has to stand apart to be found on its own.
        var pose = group
            ? $"They are relaxed and happy together, with genuine warm smiles, like a candid {mood} photo. They stand close together side by side, facing the camera with natural, easy postures, weight shifted on one leg, but with a little space between them so their t-shirts do not touch or overlap. Arms hang loosely or hands rest in their jeans pockets; no arm or hand covers anyone's chest. Framed from the top of the heads to mid-thigh."
            : "They are relaxed and at ease, with a genuine soft smile, like a candid lifestyle photo. They stand in a natural, easy pose facing the camera, body turned only slightly, weight shifted on one leg, arms hanging loosely or one hand in a jeans pocket, so the whole front of the t-shirt stays visible and nothing covers the chest. Framed from the top of the head to mid-thigh.";
        // The number of people is said outright: a looser description of a group lets the generator add more.
        var people = count == 1 ? "There is exactly one person in the photo." : $"There are exactly {Words[count]} people in the photo, no more.";
        var prompt =
            $"A natural, photorealistic lifestyle photograph of {subject}. {people} {wearing} {pose} " +
            "Plain medium grey seamless backdrop, soft natural window light from one side with gentle shadows, sharp focus on the people, natural skin and realistic fabric folds. " +
            "No other people, no text, no logos, no watermark.";
        return (prompt, group ? "4:3" : "3:4");
    }

    private static string PersonSubject(string? ageGroup, string? gender)
    {
        var female = gender?.Trim().ToLowerInvariant() == "female";
        return ageGroup?.Trim().ToLowerInvariant() switch
        {
            "child" => female ? "a girl about 8 years old" : "a boy about 8 years old",
            "teen" => female ? "a teenage girl about 15 years old" : "a teenage boy about 15 years old",
            "senior" => female ? "a woman in her late sixties with grey hair" : "a man in his late sixties with grey hair",
            _ => female ? "a woman in her late twenties" : "a man in his late twenties",
        };
    }

    // Who a group is, how many they are, and the kind of photo it should feel like.
    private static (string Subject, int Count, string Mood) GroupSubject(string? groupType) => groupType?.Trim().ToLowerInvariant() switch
    {
        "family" => ("a family of exactly three people: a mother, a father and their child about 8 years old", 3, "family"),
        "couple" => ("a couple of exactly two people in their late twenties: one woman and one man", 2, "couple"),
        _ => ("exactly three friends in their twenties: two women and one man", 3, "friends"),
    };

    private static readonly string[] Words = ["no", "one", "two", "three", "four", "five", "six"];
}
