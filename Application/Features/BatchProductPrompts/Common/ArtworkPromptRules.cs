using System.Text.RegularExpressions;

namespace APCS.Application.Features.BatchProductPrompts.Common;

/// <summary>
/// Keeps an image model from spoiling a design with stray lettering. Keywords written for the listing
/// ("hiking shirt", "funny camping gift") are cut down to what can be drawn, words about the feel of the design go to
/// its mood instead, and every image request says which text, if any, belongs in the artwork: a model asked for
/// "motifs" otherwise often writes them out as small, misspelled words. The words keywords are sorted by are data, not
/// code: see <see cref="ArtworkVocabulary"/>.
/// </summary>
public static partial class ArtworkPromptRules
{
    /// <summary>The most keywords handed to a template as motifs; more crowd the design.</summary>
    public const int MaximumMotifKeywords = 5;

    /// <summary>Stands in for the motifs when a template asks for some and none can be drawn, so its sentence still reads.</summary>
    public const string FallbackMotifs = "simple themed shapes";

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
    // words. The letters stay clean and filled, never covered. The illustration's place goes between the two halves.
    private const string PhraseInstructionStart =
        "Lettering: the only text in the artwork is the exact quoted phrase, spelled exactly as written and shown once. "
        + "Add no other words, letters, numbers, labels or taglines. "
        + "Build a full, finished graphic around it: one richly detailed featured illustration in vivid, saturated full color "
        + "that fits the theme, ";

    private const string PhraseInstructionEnd =
        ", without covering any letter, with bold filled shapes and fine details, and a few supporting details, every one "
        + "drawn as a picture, never as a word or label. "
        + "Keep the letters themselves clean, filled and fully visible: ";

    // Where the featured illustration goes: each design of a product is given one place, the next one in turn, so a
    // product's designs differ in layout. Asked to frame the lettering instead (tried 2026-10-30), Ideogram printed a word
    // twice or put icons inside the letters, so only these two are used.
    private static readonly (string Name, string Wording)[] Placements =
    [
        ("above", "placed above the lettering"),
        ("below", "placed below the lettering")
    ];

    private const string OpenPlacement = "set around the lettering (above it, below it or framing it)";

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
    public sealed record KeywordParts(string Motifs, string Mood)
    {
        /// <summary>
        /// What of the keywords is not drawn, in the Seller's order: words about the product or who it is for, praise,
        /// keywords that ask for lettering (whole), and motifs past the first <see cref="MaximumMotifKeywords"/>. Shown to
        /// the Seller, who can then put a keyword another way.
        /// </summary>
        public IReadOnlyList<string> Ignored { get; init; } = [];
    }

    /// <summary>
    /// Sorts a product's keywords. A keyword asking for lettering ("positive quote") is left out whole; in any other,
    /// words for the product ("shirt", "wall art"), for who it is for ("mom", "friends"), praise ("best") and bare numbers
    /// are dropped, and words for the feel ("funny", "vintage") go to the mood. What is left is a motif ("funny dog mom"
    /// becomes the motif "dog" and the mood "playful"), unless it is describing words only ("geometric", "coastal"), which
    /// go to the mood too. Repeated motifs are dropped. The words come from <see cref="ArtworkVocabulary"/>.
    /// </summary>
    public static KeywordParts SplitKeywords(string? keywords)
    {
        if (string.IsNullOrWhiteSpace(keywords)) return new(string.Empty, string.Empty);

        var motifs = new List<string>();
        var moods = new List<string>();
        var ignored = new List<string>();
        foreach (var keyword in keywords.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            // The template decides which words are printed: a model asked for "positive quote" writes one. Only the feel
            // it names, if any, still counts.
            if (Tokens(keyword).Any(word => Has(ArtworkVocabulary.TextWords, Key(word))))
            {
                foreach (var word in Tokens(keyword))
                    if (ArtworkVocabulary.MoodWords.TryGetValue(Key(word), out var feel)) AddOnce(moods, feel);
                AddOnce(ignored, keyword);
                continue;
            }

            // Product words several words long go first, so none of their words is drawn ("wall art" leaves no wall).
            var text = keyword;
            foreach (var phrase in new[] { TShirt(), ArtworkVocabulary.ProductPhrases })
            {
                foreach (Match match in phrase.Matches(text)) AddOnce(ignored, match.Value);
                text = phrase.Replace(text, " ");
            }

            var kept = new List<string>();
            foreach (var word in Tokens(text))
            {
                var key = Key(word);
                if (ArtworkVocabulary.MoodWords.TryGetValue(key, out var mood)) AddOnce(moods, mood);
                else if (ArtworkVocabulary.FillerWords.Contains(key)) continue;
                else if (IsNotDrawn(key)) AddOnce(ignored, word);
                else kept.Add(word);
            }
            if (kept.Count == 0) continue;

            // Describing words alone say how the design feels; with a thing they describe it ("stormy sea", "national park").
            if (kept.All(word => ArtworkVocabulary.Adjectives.Contains(Key(word))))
            {
                foreach (var word in kept) AddOnce(moods, Key(word));
                continue;
            }

            var motif = string.Join(' ', kept);
            if (motifs.Contains(motif, StringComparer.OrdinalIgnoreCase)) continue;
            if (motifs.Count < MaximumMotifKeywords) motifs.Add(motif);
            else AddOnce(ignored, motif);
        }

        return new(string.Join(", ", motifs), moods.Count == 0 ? string.Empty : $"{string.Join(", ", moods)} feel")
        {
            Ignored = ignored
        };
    }

    // A word that names the product, who it is for, or nothing a picture could show.
    private static bool IsNotDrawn(string key) =>
        Has(ArtworkVocabulary.ProductWords, key)
        || Has(ArtworkVocabulary.PeopleWords, key)
        || Has(ArtworkVocabulary.FamilyWords, key)
        || Has(ArtworkVocabulary.EmptyWords, key)
        || (key.Length > 0 && key.All(char.IsDigit));

    private static string[] Tokens(string text) =>
        text.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    // How a word is looked up: lower case, without the punctuation around it or a possessive ("mom's" is "mom").
    private static string Key(string word)
    {
        var key = word.Trim().Trim(KeyTrim).ToLowerInvariant();
        return key.EndsWith("'s", StringComparison.Ordinal) || key.EndsWith("’s", StringComparison.Ordinal) ? key[..^2] : key;
    }

    private static readonly char[] KeyTrim = ['.', ',', ';', ':', '!', '?', '"', '“', '”', '(', ')', '[', ']', '\'', '‘', '’'];

    private static bool Has(IReadOnlySet<string> words, string key) => words.Contains(key) || words.Contains(Singular(key));

    private static void AddOnce(List<string> list, string value)
    {
        if (!list.Contains(value, StringComparer.OrdinalIgnoreCase)) list.Add(value);
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

    // With a phrase template, the keywords and description less what repeats the phrase. Words quoted in the description
    // are left out unless they are the phrase: only the phrase is printed, and a model asked for quoted words draws them.
    private static (string Keywords, string Description) PhraseFitted(PromptComponents components)
    {
        var phrase = components.Subject.Trim();
        var printsPhrase = PrintsExactPhrase(components.BasePrompt) && phrase.Length > 0;
        var description = WithoutOtherQuotedText(components.Description, printsPhrase ? phrase : null);
        if (!description.Any(char.IsLetterOrDigit)) description = string.Empty;
        return printsPhrase
            ? (WithoutPhraseWords(components.Keywords, phrase), WithoutPhrase(description, phrase))
            : (components.Keywords, description);
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
    /// <param name="design">
    /// Which of the product's designs this is, from 0: with a phrase template it decides where the featured illustration
    /// goes (<see cref="IllustrationPlacement"/>). Without it the model chooses.
    /// </param>
    public static string LetteringInstruction(string? basePrompt, bool onGarment = true, bool ownPrompt = false, int? design = null)
    {
        if (!PrintsExactPhrase(basePrompt)) return MotifInstruction;
        if (ownPrompt) return OwnPromptPhraseInstruction + (onGarment ? OwnPromptGarmentLettering : OwnPromptBackdropLettering);

        var placement = design is int index ? Placements[index % Placements.Length].Wording : OpenPlacement;
        return PhraseInstructionStart + placement + PhraseInstructionEnd + (onGarment ? GarmentLettering : BackdropLettering);
    }

    /// <summary>
    /// Where <see cref="LetteringInstruction"/> puts the featured illustration of a product's design ("above" or "below"
    /// the lettering), or null when it leaves that to the model or the Seller: a template that prints no phrase,
    /// the Seller's own prompt, or no design number.
    /// </summary>
    public static string? IllustrationPlacement(string? basePrompt, bool ownPrompt, int? design) =>
        PrintsExactPhrase(basePrompt) && !ownPrompt && design is int index ? Placements[index % Placements.Length].Name : null;

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
    // where or how they are lettered ("underneath" ending a clause, "in bold letters"). A quote mark glued to a number or
    // word, or one followed by a space, is not an opening quote: 12" x 18" is a size.
    [GeneratedRegex(@"(?:(?:\s*,\s*|\s+)(?:and|with|reading|saying|plus)\b)?(?:(?:^|\s+)the\b)?(?:(?:^|\s+)(?:text|words?|phrase|slogan|quote|saying|lettering|title|caption)\b)?\s*:?\s*(?<![\p{L}\p{N}])[""“](?<text>[^""“”\s][^""“”]*)[""”]" +
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
