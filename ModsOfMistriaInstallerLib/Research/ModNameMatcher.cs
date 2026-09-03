using System.Text.RegularExpressions;

namespace Garethp.ModsOfMistriaInstallerLib.Research;

/// <summary>
/// Decides whether a piece of text is talking about a particular mod.
///
/// The old check was <c>text.Contains(name)</c>, which finds a mention only when someone typed the
/// mod's title exactly as Nexus has it. Almost nobody does. A folder is called
/// <c>suushiico_witchy_tools</c>, a commenter writes "the witchy weapons mod", the author writes
/// "Witchy Weapons and Tools" without the possessive, and none of those matched "Sushi's Witchy
/// Weapons and Tools". Meanwhile the same check would happily match a mod actually called "Tools"
/// against every sentence containing the word.
///
/// So a name is reduced to the words that make it distinctive - dropping punctuation, possessives,
/// the game's own name and the filler words every mod title contains - and a mention is a text that
/// carries enough of those words to not be a coincidence.
/// </summary>
public static class ModNameMatcher
{
    /// <summary>
    /// Words that appear in so many mod titles that matching on one alone means nothing.
    /// </summary>
    private static readonly HashSet<string> Filler = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "and", "or", "of", "for", "to", "with", "by", "in", "on",
        "mod", "mods", "modded", "momi", "aim", "installer", "fields", "mistria",
        "fieldsofmistria", "fom", "version", "edition", "pack", "set", "update", "fix",
        "patch", "new", "custom", "more", "better", "improved", "remake", "port"
    };

    private static readonly Regex Separators = new(@"[^\p{L}\p{Nd}]+", RegexOptions.Compiled);

    /// <summary>Trailing possessives, so "Sushi's" and "Sushi" are the same word.</summary>
    private static readonly Regex Possessive = new(@"(?:'s|s')$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The words from a mod's name that are worth searching for. Empty when the title is made
    /// entirely of filler, in which case no mention can be claimed at all.
    /// </summary>
    public static IReadOnlyList<string> DistinctiveWords(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return [];

        return Separators.Split(name)
            .Select(word => Possessive.Replace(word, ""))
            .Where(word => word.Length >= 3 && !Filler.Contains(word))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// True when <paramref name="text"/> is plausibly talking about the mod called
    /// <paramref name="name"/>.
    ///
    /// One distinctive word is enough only when the title has just one to give - a mod called
    /// "Chromatic" is genuinely identified by the word "chromatic". Past that, two thirds of the
    /// distinctive words must be present, which lets "witchy weapons" find "Sushi's Witchy Weapons
    /// and Tools" while keeping a stray "weapons" from doing it.
    /// </summary>
    public static bool Mentions(string text, string name)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        var words = DistinctiveWords(name);
        if (words.Count == 0) return false;

        // Punctuation and underscores are flattened on both sides, so a folder name like
        // "suushiico_witchy_tools" reads as the words it is made of.
        var haystack = " " + Separators.Replace(text, " ") + " ";

        var present = words.Count(word => AppearsAsWord(haystack, word));

        // Half the distinctive words, and never fewer than two.
        //
        // Two thirds was the first attempt and it was too strict for how people actually ask the
        // question: "does this work with Witchy Weapons?" carries two of this mod's four words and
        // is unmistakably about it, but fails a three-of-four bar. Two co-occurring distinctive
        // words is already a strong signal - it is one word that means nothing, which is why the
        // floor never drops below two for a title that has that many to give.
        var needed = words.Count switch
        {
            1 => 1,
            2 => 2,
            _ => Math.Max(2, (int)Math.Ceiling(words.Count / 2.0))
        };

        return present >= needed;
    }

    /// <summary>
    /// Drops a plural so that a title's "Weapons" can find a comment's "weapon".
    ///
    /// Matching allows the text's word to be a little longer than the title's, which handles
    /// "weapon" finding "weapons" - but not the reverse, and people drop plurals constantly when
    /// referring to a mod. Searching for the singular covers both directions at once.
    /// </summary>
    private static string Stem(string word)
    {
        if (word.Length < 5) return word;
        if (word.EndsWith("es", StringComparison.OrdinalIgnoreCase)) return word[..^2];
        if (word.EndsWith("s", StringComparison.OrdinalIgnoreCase)) return word[..^1];
        return word;
    }

    /// <summary>
    /// Finds the word at a word boundary, allowing a short inflection - "weapon" finding "weapons"
    /// - without letting a short word match the middle of a longer unrelated one.
    /// </summary>
    private static bool AppearsAsWord(string haystack, string word)
    {
        var needle = " " + Stem(word);
        var at = haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase);

        while (at >= 0)
        {
            var after = at + needle.Length;

            // At most two trailing letters: "weapons", "tooling" - not "weaponsmith".
            var extra = 0;
            while (after + extra < haystack.Length && char.IsLetter(haystack[after + extra])) extra++;
            if (extra <= 2) return true;

            at = haystack.IndexOf(needle, at + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>
    /// Which of <paramref name="names"/> the text mentions, if any. Used to attribute a comment to
    /// the pairing it is actually about.
    /// </summary>
    public static string? FirstMentioned(string text, IEnumerable<string> names) =>
        names.FirstOrDefault(name => Mentions(text, name));
}
