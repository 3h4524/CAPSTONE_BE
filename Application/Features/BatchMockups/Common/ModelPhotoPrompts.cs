namespace APCS.Application.Features.BatchMockups.Common;

/// <summary>
/// The prompt a model photo is made from with AI. It is put together only from fixed wording for the chosen product,
/// people and style, never from free text, so every photo stays what the no-AI mock-up compositing needs: people
/// facing the camera, the plain white garment whole and uncovered, on a background it stands out from.
/// </summary>
public static class ModelPhotoPrompts
{
    /// <summary>The prompt and the aspect ratio for a photo with these attributes and style (checked beforehand).</summary>
    /// <param name="attributes">Who is in the photo.</param>
    /// <param name="productType">'tshirt' or 'hoodie'.</param>
    /// <param name="style">Where the photo is taken, how the people are dressed and stand; null is the default.</param>
    /// <param name="random">
    /// Picks the people's looks (heritage, hair, build) from fixed lists, so two photos with the same choices differ; null
    /// leaves them to the generator.
    /// </param>
    public static (string Prompt, string AspectRatio) Build(
        ModelAttributesDto attributes, string productType = "tshirt", ModelPhotoStyleDto? style = null, Random? random = null)
    {
        var group = attributes.People.Trim().ToLowerInvariant() == ModelPeople.Group;
        var clean = ModelPhotoStyles.Clean(style);
        var product = Product(productType);
        var (subject, count, mood) = group
            ? GroupSubject(attributes.GroupType, random)
            : (PersonSubject(attributes.AgeGroup, attributes.Gender, random), 1, "");

        // The number of people is said outright: a looser description of a group lets the generator add more.
        var people = count == 1 ? "There is exactly one person in the photo." : $"There are exactly {Words[count]} people in the photo, no more.";
        // Said outright too: left to itself, the generator sometimes puts the people off to one side.
        var framing = $"{(group ? "The group is" : "They are")} centered in the frame, with equal space to their left and right, the camera directly in front of them.";
        var prompt =
            $"A natural, photorealistic lifestyle photograph of {subject}. {people} {framing} {Dress(product, clean.Outfit!, group)} " +
            $"{Pose(product, clean.Pose!, group, mood)} {Scene(clean.Scene!)}, natural skin and realistic fabric. " +
            $"Nothing behind them is white, so the {product.Noun} stands out. No other people, no text, no logos, no watermark.";
        return (prompt, group ? "4:3" : "3:4");
    }

    // What the design is printed on: its name, and how it is described.
    private sealed record Printed(string Noun, string Plural, string Garment);

    private static Printed Product(string productType) => productType.Trim().ToLowerInvariant() switch
    {
        "hoodie" => new("hoodie", "hoodies",
            "plain white pullover hoodie with no print, logo, text or pattern, hood down behind the neck, with no zip and no drawstrings over the chest"),
        _ => new("t-shirt", "t-shirts", "plain white crew-neck short-sleeve cotton t-shirt with no print, logo, text or pattern, untucked"),
    };

    // The garment is worn with the outfit's trousers and shoes.
    private static string Dress(Printed product, string outfit, bool group)
    {
        var bottoms = outfit switch
        {
            "streetwear" => "loose cargo trousers and chunky sneakers",
            "minimal" => "tailored black trousers",
            "sporty" => "black joggers and running shoes",
            _ => "dark jeans",
        };
        return group
            ? $"Everyone wears the same {product.Garment}, with {bottoms}."
            : $"They wear a {product.Garment}, with {bottoms}.";
    }

    // Relaxed and natural, but every garment's front stays in view and unhidden: the design is printed there, and on
    // a group each person's has to stand apart to be found on its own.
    private static string Pose(Printed product, string pose, bool group, string mood)
    {
        var kept = $"the whole front of the {product.Noun} stays visible and nothing covers the chest";
        if (group)
        {
            var apart = $"with a little space between them so their {product.Plural} do not touch or overlap";
            return pose == "walking"
                ? $"They walk toward the camera side by side, relaxed and laughing together, like a candid {mood} photo, {apart}. Arms swing gently at their sides; {kept}. Framed from the top of the heads to the knees."
                : $"They are relaxed and happy together, with genuine warm smiles, like a candid {mood} photo. They stand close together side by side, facing the camera with natural, easy postures, weight shifted on one leg, but {apart}. Arms hang loosely or hands rest in pockets; {kept}. Framed from the top of the heads to mid-thigh.";
        }

        return pose switch
        {
            "walking" => $"They walk toward the camera mid-stride, relaxed with a genuine soft smile, like a candid lifestyle photo, body facing the camera and arms swinging gently at their sides, so {kept}. Framed from the top of the head to the knees.",
            "leaning" => $"They lean one shoulder casually against a wall, relaxed with a genuine soft smile, body still turned toward the camera, arms loose or one hand in a pocket, so {kept}. Framed from the top of the head to mid-thigh.",
            "seated" => $"They sit upright on a simple stool, relaxed with a genuine soft smile, facing the camera with their hands resting on their knees, so {kept}. Framed from the top of the head to the knees.",
            _ => $"They are relaxed and at ease, with a genuine soft smile, like a candid lifestyle photo. They stand in a natural, easy pose facing the camera, body turned only slightly, weight shifted on one leg, arms hanging loosely or one hand in a pocket, so {kept}. Framed from the top of the head to mid-thigh.",
        };
    }

    private const string PeopleInFocus = "sharp focus on the people";

    // Says what is wanted rather than naming blur: a word in the prompt tends to bring that thing into the photo.
    private const string AllInFocus = "shot at f/11 with deep depth of field, the people and everything behind them in sharp focus";

    // A studio's plain backdrop, or a real place kept sharp from front to back.
    private static string Scene(string scene) => scene switch
    {
        "studio_beige" => $"Plain warm beige seamless backdrop, soft natural window light from one side with gentle shadows, {PeopleInFocus}",
        "studio_dark" => $"Plain dark charcoal seamless backdrop, soft studio light from one side with gentle shadows, {PeopleInFocus}",
        "street" => $"Outdoors on a quiet city street in soft daylight, with buildings behind them, {AllInFocus}",
        "cafe" => $"Inside a bright, cosy café in warm natural light, with the interior behind them, {AllInFocus}",
        "park" => $"Outdoors in a green park in soft daylight, with trees behind them, {AllInFocus}",
        _ => $"Plain medium grey seamless backdrop, soft natural window light from one side with gentle shadows, {PeopleInFocus}",
    };

    private static string PersonSubject(string? ageGroup, string? gender, Random? random)
    {
        var female = gender?.Trim().ToLowerInvariant() == "female";
        var age = ageGroup?.Trim().ToLowerInvariant();
        var who = age switch
        {
            "child" => female ? "a girl about 8 years old" : "a boy about 8 years old",
            "teen" => female ? "a teenage girl about 15 years old" : "a teenage boy about 15 years old",
            "senior" => female ? "a woman in her late sixties" : "a man in his late sixties",
            _ => female ? "a woman in her late twenties" : "a man in his late twenties",
        };
        if (random is null)
            return age == "senior" ? $"{who} with grey hair" : who;

        var hair = age switch
        {
            "senior" => female ? SeniorWomenHair : SeniorMenHair,
            "child" => female ? GirlsHair : BoysHair,
            _ => female ? WomenHair : age == "teen" ? BoysHair : MenHair,
        };
        var build = age is "child" or "teen" ? "" : $", {Builds[random.Next(Builds.Length)]}";
        return $"{who} of {Heritages[random.Next(Heritages.Length)]} descent, with {hair[random.Next(hair.Length)]}{build}";
    }

    // Who a group is, how many they are, and the kind of photo it should feel like.
    private static (string Subject, int Count, string Mood) GroupSubject(string? groupType, Random? random)
    {
        var (subject, count, mood) = groupType?.Trim().ToLowerInvariant() switch
        {
            "family" => ("a family of exactly three people: a mother, a father and their child about 8 years old", 3, "family"),
            "couple" => ("a couple of exactly two people in their late twenties: one woman and one man", 2, "couple"),
            _ => ("exactly three friends in their twenties: two women and one man", 3, "friends"),
        };
        if (random is null)
            return (subject, count, mood);
        // A family shares its heritage; friends or a couple may each have their own.
        var heritage = mood != "family" && random.Next(2) == 0 ? "of mixed backgrounds" : $"of {Heritages[random.Next(Heritages.Length)]} descent";
        return ($"{subject}, {heritage}", count, mood);
    }

    private static readonly string[] Heritages = ["East Asian", "Southeast Asian", "South Asian", "African", "Latin American", "Middle Eastern", "European"];
    private static readonly string[] Builds = ["a slim build", "an average build", "an athletic build", "a sturdy build"];
    private static readonly string[] WomenHair =
        ["long straight dark hair", "shoulder-length wavy brown hair", "loose natural curls", "a short bob", "hair tied back in a ponytail", "long braids"];
    private static readonly string[] MenHair =
        ["short dark hair", "short curly hair", "a buzz cut", "wavy medium-length hair", "short hair and a neat beard", "a short fade"];
    private static readonly string[] SeniorWomenHair = ["short grey hair", "silver hair in a neat bob", "grey hair tied back"];
    private static readonly string[] SeniorMenHair = ["short grey hair", "grey hair and a trimmed grey beard", "thinning grey hair"];
    private static readonly string[] GirlsHair = ["long hair in a ponytail", "short curly hair", "hair in two braids", "shoulder-length straight hair"];
    private static readonly string[] BoysHair = ["short hair", "short curly hair", "messy medium-length hair", "a short fade"];

    private static readonly string[] Words = ["no", "one", "two", "three", "four", "five", "six"];
}
