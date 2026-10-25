using System.Text.RegularExpressions;

namespace APCS.Application.Features.BatchProductPrompts.Common;

/// <summary>
/// Keeps an image model from spoiling a design with stray lettering. Keywords written for the listing
/// ("hiking shirt", "funny camping gift") are cut down to what can be drawn, words about the feel of the design go to
/// its mood instead, and every image request says which text, if any, belongs in the artwork: a model asked for
/// "motifs" otherwise often writes them out as small, misspelled words.
/// </summary>
public static partial class ArtworkPromptRules
{
    /// <summary>The most keywords handed to a template as motifs; more crowd the design.</summary>
    public const int MaximumMotifKeywords = 5;

    /// <summary>Stands in for the motifs when a template asks for some and none can be drawn, so its sentence still reads.</summary>
    public const string FallbackMotifs = "simple themed shapes";

    // Words about the listing (the product, its buyer, the sale) rather than anything to draw.
    private static readonly HashSet<string> ListingWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "shirt", "shirts", "tshirt", "tshirts", "tee", "tees", "hoodie", "hoodies", "sweatshirt", "sweatshirts",
        "sweater", "mug", "mugs", "poster", "posters", "tote", "totes", "bag", "apparel", "clothing", "outfit",
        "merch", "unisex", "gift", "gifts", "present", "idea", "ideas", "lover", "lovers", "fan", "fans", "for",
        "men", "mens", "women", "womens", "him", "her", "graphic", "design", "print"
    };

    // Who the product is for, named by family tie or not at all: a model writes the word out or draws a stranger.
    // Jobs stay ("nurse", "teacher", "developer"): each has an icon people recognize.
    private static readonly HashSet<string> PersonWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "mom", "moms", "mum", "mums", "mama", "mamas", "mommy", "mother", "mothers",
        "dad", "dads", "daddy", "papa", "father", "fathers",
        "grandma", "grandmas", "grandpa", "grandpas", "granny", "nana", "grandmother", "grandfather",
        "wife", "husband", "boyfriend", "girlfriend", "sister", "sisters", "brother", "brothers",
        "son", "sons", "daughter", "daughters", "aunt", "auntie", "uncle", "kid", "kids", "boy", "boys", "girl", "girls",
        "friend", "friends", "bestie", "besties", "people", "person", "everyone", "enthusiast", "enthusiasts",
        "owner", "owners"
    };

    // The feel of a design rather than something in it, and the mood it becomes; a null mood says nothing a picture could show.
    private static readonly Dictionary<string, string?> ToneWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["funny"] = "playful", ["humor"] = "playful", ["humour"] = "playful", ["humorous"] = "playful",
        ["hilarious"] =  "playful", ["playful"] = "playful",
        ["sarcastic"] = "sassy", ["sarcasm"] = "sassy", ["sassy"] = "sassy",
        ["cute"] = "cute", ["kawaii"] = "cute", ["adorable"] = "cute",
        ["vintage"] = "vintage", ["retro"] = "retro", ["classic"] = "classic",
        ["boho"] = "boho", ["spooky"] = "spooky", ["cozy"] = "cozy", ["cosy"] = "cozy",
        ["minimalist"] = "minimalist", ["minimal"] = "minimalist",
        ["inspirational"] = "uplifting", ["motivational"] = "uplifting", ["inspiring"] = "uplifting",
        ["cool"] = null, ["aesthetic"] = null, ["trendy"] = null, ["awesome"] = null, ["best"] = null
    };

    /// <summary>A description shorter than this says too little about the design to go on.</summary>
    public const int ThinDescriptionWords = 5;

    /// <summary>A custom image prompt shorter than this says too little about the design to go on.</summary>
    public const int ThinCustomPromptWords = 8;

    // What a design made from the Seller's own prompt avoids, there being no template to say: with lettering, its usual
    // faults; without, any lettering at all. Either way no real brand's logo, which a design for sale must not carry.
    private const string CustomLetteringAvoid = "misspelled words, extra words, distorted letters, brand logos, watermark, product mockup";

    private const string CustomPictureAvoid = "text, letters, words, brand logos, watermark, signature, product mockup";

    // Asks for motifs by the product's theme when its keywords leave nothing to draw, and lets the image model work out
    // what stands for it ("simple icons that suit Pickleball players"): no list of themes to keep up.
    private const string ThemedMotifsPrefix = "simple icons that suit ";

    // Asks for a finished graphic, not lettering with a few tiny icons: one detailed, full-color featured illustration set
    // around the lettering and some supporting details, every one of them a picture, so the richness never comes from extra
    // words. The letters stay clean and filled, never covered.
    private const string PhraseInstruction =
        "Lettering: the only text in the artwork is the exact quoted phrase, spelled exactly as written and shown once. "
        + "Add no other words, letters, numbers, labels or taglines. "
        + "Build a full, finished graphic around it: one richly detailed featured illustration in vivid, saturated full color "
        + "that fits the theme, set around the lettering (above it, below it or framing it) without covering any letter, "
        + "with bold filled shapes and fine details, and a few supporting details, every one drawn as a picture, never as a "
        + "word or label. "
        + "Keep the letters themselves clean, filled and fully visible: ";

    // A print on a garment or bag has no backdrop and may go on any color: its letters keep bold colors inside a sticker-style
    // double outline (thick dark, thin light), which reads on light and dark products alike without washing the design out.
    // Anything else is drawn on its own backdrop, which the letters only have to stand out from.
    private const string GarmentLettering =
        "bold solid colors inside a sticker-style double outline, a thick dark outline with a thin light edge around it, "
        + "so they stay readable on both light and dark products.";

    private const string BackdropLettering = "solid colors that contrast strongly with the background, with a clean outline.";

    // With the Seller's own prompt the look is theirs (style, colors, layout, how much detail): only what keeps the words
    // right is added, so nothing here pulls against what they asked for.
    private const string OwnPromptPhraseInstruction =
        "Lettering: the only text in the artwork is the exact quoted phrase, spelled exactly as written and shown once. "
        + "Add no other words, letters, numbers, labels or taglines, and draw everything else as a picture, never as a word "
        + "or label. Keep every letter whole, fully visible with nothing covering it, and ";

    private const string OwnPromptGarmentLettering = "readable on both light and dark products.";

    private const string OwnPromptBackdropLettering = "readable against the background.";

    private const string MotifInstruction = "Draw every motif as a picture, never as a word or label.";

    /// <summary>
    /// Sent with every design, whatever the prompt asks: a print must not carry a brand's mark. Said as what to draw,
    /// since an "Avoid: brand logos" line alone still let Ideogram badge a surf van as a Volkswagen. "On it" keeps it to
    /// the things drawn, so a badge-style design is still fine.
    /// </summary>
    public const string UnbrandedInstruction =
        "Draw every vehicle, product and object as a generic, unbranded one, with no logos, emblems or brand marks on it.";

    /// <summary>A product's keywords sorted into what a template can draw and the feel they ask for.</summary>
    /// <param name="Motifs">Comma-separated things to draw, at most <see cref="MaximumMotifKeywords"/>, in the Seller's order.</param>
    /// <param name="Mood">The feel the keywords name ("playful, vintage feel"), or empty.</param>
    public sealed record KeywordParts(string Motifs, string Mood);

    /// <summary>
    /// Sorts a product's keywords word by word: listing words and words for a person ("mom", "friends") are dropped,
    /// words about the feel ("funny", "vintage") go to the mood, and what is left of each keyword is a motif
    /// ("funny dog mom" becomes the motif "dog" and the mood "playful"). Empty and repeated motifs are dropped.
    /// </summary>
    public static KeywordParts SplitKeywords(string? keywords)
    {
        if (string.IsNullOrWhiteSpace(keywords)) return new(string.Empty, string.Empty);

        var motifs = new List<string>();
        var moods = new List<string>();
        foreach (var keyword in keywords.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var drawn = new List<string>();
            foreach (var word in TShirt().Replace(keyword, " ").Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (ListingWords.Contains(word) || PersonWords.Contains(word)) continue;
                if (ToneWords.TryGetValue(word, out var mood))
                {
                    if (mood is not null && !moods.Contains(mood)) moods.Add(mood);
                    continue;
                }
                drawn.Add(word);
            }

            var motif = string.Join(' ', drawn);
            if (motif.Length > 0 && motifs.Count < MaximumMotifKeywords && !motifs.Contains(motif, StringComparer.OrdinalIgnoreCase))
                motifs.Add(motif);
        }

        return new(string.Join(", ", motifs), moods.Count == 0 ? string.Empty : $"{string.Join(", ", moods)} feel");
    }

    /// <summary>The motifs of <see cref="SplitKeywords"/>.</summary>
    public static string MotifKeywords(string? keywords) => SplitKeywords(keywords).Motifs;

    /// <summary>
    /// Fits the prompt pieces to the words the design prints, once the Seller's own changes are applied. With a phrase
    /// template, a motif the phrase already says and any repeat of the phrase in the description are dropped, because
    /// the model would write them out a second time. A template that asks for motifs and has none left gets the motifs
    /// for the product's niche, left to the image model to pick, or a plain stand-in when the product names no niche.
    /// </summary>
    public static PromptComponents Fit(PromptComponents components)
    {
        var (keywords, description) = PhraseFitted(components);
        if (keywords.Trim().Length == 0 && UsesKeywords().IsMatch(components.BasePrompt))
        {
            var niche = components.Niche.Trim();
            keywords = niche.Length > 0 && !string.Equals(niche, PromptRules.FallbackNiche, StringComparison.OrdinalIgnoreCase)
                ? ThemedMotifsPrefix + niche
                : FallbackMotifs;
        }
        return components with { Keywords = keywords, Description = description };
    }

    /// <summary>
    /// True when the Seller gave next to nothing to design from (before <see cref="Fit"/>): no keyword left to draw, a
    /// description of a few words at most, and no instructions. A provider that can fill such a prompt out itself is
    /// then asked to.
    /// </summary>
    public static bool IsThin(PromptComponents components)
    {
        var (keywords, description) = PhraseFitted(components);
        return keywords.Trim().Length == 0
            && Words(description).Count() < ThinDescriptionWords
            && components.Instructions.Trim().Length == 0;
    }

    // With a phrase template, the keywords and description less what repeats the phrase.
    private static (string Keywords, string Description) PhraseFitted(PromptComponents components)
    {
        var phrase = components.Subject.Trim();
        return PrintsExactPhrase(components.BasePrompt) && phrase.Length > 0
            ? (WithoutPhraseWords(components.Keywords, phrase), WithoutPhrase(components.Description, phrase))
            : (components.Keywords, components.Description);
    }

    /// <summary>True when the template prints the subject as an exact quoted phrase, as typography templates do.</summary>
    public static bool PrintsExactPhrase(string? basePrompt) => basePrompt is not null && QuotedSubject().IsMatch(basePrompt);

    /// <summary>
    /// Sent with every design request, after the stored prompt rather than inside it (so it never counts toward
    /// the editable prompt's length): with a phrase template, that phrase is the only lettering; with any other,
    /// motifs are drawn, not written.
    /// </summary>
    /// <param name="basePrompt">The template prompt the design was made from (<c>AiPrompt.SystemPrompt</c>).</param>
    /// <param name="onGarment">The design is printed on its own on a garment or bag, without a backdrop of its own.</param>
    /// <param name="ownPrompt">
    /// The design is made from the Seller's own image prompt: the lettering is kept right, but its look is left to them.
    /// </param>
    public static string LetteringInstruction(string? basePrompt, bool onGarment = true, bool ownPrompt = false)
    {
        if (!PrintsExactPhrase(basePrompt)) return MotifInstruction;
        return ownPrompt
            ? OwnPromptPhraseInstruction + (onGarment ? OwnPromptGarmentLettering : OwnPromptBackdropLettering)
            : PhraseInstruction + (onGarment ? GarmentLettering : BackdropLettering);
    }

    /// <summary>The line of things to avoid for a design made from the Seller's own prompt, which has no template's to use.</summary>
    /// <param name="basePrompt">The prompt the design was made from (<c>AiPrompt.SystemPrompt</c>): it quotes the design text when there is one.</param>
    public static string CustomAvoidInstruction(string? basePrompt) =>
        AvoidInstruction(PrintsExactPhrase(basePrompt) ? CustomLetteringAvoid : CustomPictureAvoid);

    /// <summary>
    /// The Seller's own image prompt without quoted words other than the design text, and the words leading up to them
    /// ("... and the words "EST 1985""): only the design text is printed, and a model asked for quoted words draws them.
    /// </summary>
    public static string WithoutOtherQuotedText(string customPrompt, string? designText)
    {
        var keep = designText?.Trim() ?? string.Empty;
        var cleaned = QuotedTextWithLeadIn().Replace(customPrompt, match =>
            keep.Length > 0 && string.Equals(match.Groups["text"].Value.Trim(), keep, StringComparison.OrdinalIgnoreCase)
                ? match.Value
                : string.Empty);
        if (cleaned == customPrompt) return customPrompt;

        cleaned = MultipleSpaces().Replace(cleaned, " ");
        cleaned = SpaceBeforePunctuation().Replace(cleaned, "$1");
        cleaned = CommaBeforeStop().Replace(cleaned, "$1");
        return cleaned.Trim().TrimStart(',', ';', ':', '-', '.').Trim();
    }

    /// <summary>True when the Seller's own image prompt is a few words at most, too little to design from unaided.</summary>
    public static bool IsThinCustomPrompt(string? customPrompt) => Words(customPrompt ?? string.Empty).Count() < ThinCustomPromptWords;

    /// <summary>
    /// The template's own negative terms as a line sent with the request ("Avoid: ..."), or empty when it has none.
    /// Placeholders are dropped: what they stand for is in the prompt itself.
    /// </summary>
    public static string AvoidInstruction(string? negativePrompt)
    {
        if (string.IsNullOrWhiteSpace(negativePrompt)) return string.Empty;
        var terms = Placeholder().Replace(negativePrompt, " ")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(term => MultipleSpaces().Replace(term, " ").TrimEnd('.'))
            .Where(term => term.Length > 0)
            .ToList();
        return terms.Count == 0 ? string.Empty : $"Avoid: {string.Join(", ", terms)}.";
    }

    // A motif goes when every one of its words is in the phrase ("coffee" for "Coffee Then Code"); plural or not.
    private static string WithoutPhraseWords(string keywords, string phrase)
    {
        var phraseWords = Words(phrase).Select(Singular).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var kept = keywords.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(motif => !Words(motif).All(word => phraseWords.Contains(Singular(word))));
        return string.Join(", ", kept);
    }

    // "... and the text Coffee Then Code." loses the phrase together with the words leading up to it; a phrase standing
    // bare in a sentence ("lettering of Coffee Then Code above a cup") becomes "the phrase", so the sentence still reads.
    private static string WithoutPhrase(string description, string phrase)
    {
        var words = Words(phrase).ToList();
        if (description.Length == 0 || words.Count == 0) return description;

        var spelled = string.Join(@"[\s\p{P}]*", words.Select(Regex.Escape));
        var pattern = @"(?<lead>(?:(?:\s*,\s*|\s+)(?:and|with|featuring|showing|reading|saying|plus)\b)?"
            + @"(?:(?:^|\s+)the\b)?"
            + @"(?:(?:^|\s+)(?:text|words?|phrase|slogan|quote|saying|lettering|title|caption)\b)?)"
            + @"(?<gap>\s*:?\s*)[""“”'‘’]?(?<![\p{L}\p{N}])" + spelled + @"(?![\p{L}\p{N}])[""“”'‘’]?";
        var cleaned = Regex.Replace(description, pattern,
            match => match.Groups["lead"].Length > 0 ? string.Empty
                : match.Groups["gap"].Value.Any(char.IsWhiteSpace) ? " the phrase" : "the phrase",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (cleaned == description) return description;

        cleaned = MultipleSpaces().Replace(cleaned, " ");
        cleaned = SpaceBeforePunctuation().Replace(cleaned, "$1");
        cleaned = CommaBeforeStop().Replace(cleaned, "$1");
        cleaned = cleaned.Trim().TrimStart(',', ';', ':', '-', '.').Trim();
        return cleaned.Any(char.IsLetterOrDigit) ? cleaned : string.Empty;
    }

    private static IEnumerable<string> Words(string text) => Word().Matches(text).Select(match => match.Value);

    private static string Singular(string word) =>
        word.Length > 3 && word.EndsWith('s') && !word.EndsWith("ss", StringComparison.OrdinalIgnoreCase) ? word[..^1] : word;

    [GeneratedRegex(@"\bt[\s-]?shirts?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TShirt();

    [GeneratedRegex(@"""\{\{?\s*subject\s*\}\}?""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QuotedSubject();

    [GeneratedRegex(@"\{\{?\s*keywords\s*\}\}?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UsesKeywords();

    [GeneratedRegex(@"\{\{?\s*[A-Za-z][A-Za-z0-9_]*\s*\}\}?", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"[\p{L}\p{N}']+", RegexOptions.CultureInvariant)]
    private static partial Regex Word();

    // Quoted words, straight or curly, with what leads up to them ("and", "with the words", "reading" ...) and what says
    // where or how they are lettered ("underneath" ending a clause, "in bold letters").
    [GeneratedRegex(@"(?:(?:\s*,\s*|\s+)(?:and|with|reading|saying|plus)\b)?(?:(?:^|\s+)the\b)?(?:(?:^|\s+)(?:text|words?|phrase|slogan|quote|saying|lettering|title|caption)\b)?\s*:?\s*[""“](?<text>[^""“”]*)[""”]" +
        @"(?:\s+(?:(?:underneath|beneath|below|above|on top|at the (?:top|bottom)|(?:under|below|above) it)(?=\s*(?:[,.;!?]|$))|in (?:[\p{L}-]+\s+){0,3}(?:letters|lettering|font|type|typeface|script|typography)\b))?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QuotedTextWithLeadIn();

    [GeneratedRegex(@"[ \t]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex MultipleSpaces();

    [GeneratedRegex(@"\s+([,.;:!?])", RegexOptions.CultureInvariant)]
    private static partial Regex SpaceBeforePunctuation();

    [GeneratedRegex(@",+\s*([.;:!?,])", RegexOptions.CultureInvariant)]
    private static partial Regex CommaBeforeStop();
}
