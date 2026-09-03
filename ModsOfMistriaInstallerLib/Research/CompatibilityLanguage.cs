using System.Text.RegularExpressions;

namespace Garethp.ModsOfMistriaInstallerLib.Research;

/// <summary>What a piece of evidence says about whether the mods can live together.</summary>
public enum Polarity
{
    /// <summary>The author or a user says there is no problem. Evidence *against* the conflict.</summary>
    Clearance,

    /// <summary>Something is stated to be broken or unusable together.</summary>
    Blocker,

    /// <summary>It works, but only under a condition - a patch, a load order, a requirement.</summary>
    Caution,

    /// <summary>Compatibility is discussed without a verdict either way.</summary>
    Context
}

/// <summary>One reason a sentence was kept, and what it means.</summary>
public sealed record CompatibilitySignal(Polarity Polarity, string Reason);

/// <summary>
/// Decides whether a sentence from a mod page bears on a conflict, and which way it points.
///
/// This exists because the old keyword list got the most common phrasing in the game's modding
/// scene wrong. It looked for the words "compatible", "compatibility" and "incompatible", and
/// authors overwhelmingly write the plural: "No known compatibilities / incompatibilities at this
/// time. It is a standalone mod." None of the three words is a substring of either plural, so the
/// one sentence on the page that answered the question was invisible. Matching on the stem
/// "compatib" instead catches every inflection there is.
///
/// The second thing it fixes is that the old list had no notion of direction. "Incompatible with
/// Foo" and "no known incompatibilities" both merely "mentioned compatibility", so a page that
/// explicitly cleared the mod read exactly like one that condemned it, and the user had to read
/// every quote to find out which. Here a sentence is classified, and a clearance is worth showing
/// precisely because it lets an issue be closed.
///
/// Order matters and is the whole design: negated phrasings are tested before the words they
/// negate, so "no known incompatibilities" is settled as a clearance before anything can see the
/// bare word "incompatibilities" inside it and call it a blocker.
/// </summary>
public static class CompatibilityLanguage
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.Compiled;

    /// <summary>
    /// The rules, in the order they are tried. The first that matches decides the sentence.
    /// </summary>
    private static readonly (Regex Pattern, Polarity Polarity, string Reason)[] Rules =
    [
        // ── Negated claims, first, so the negation wins over the word it negates ──────────

        // "No known compatibilities / incompatibilities at this time", "no conflicts reported",
        // "not aware of any issues". The optional "known"/"reported" and the slash-separated pair
        // are both taken straight from how authors actually write this line.
        //
        // The trailing lookahead keeps "there is no compatibility patch" out of it: that sentence
        // negates the existence of a fix, not the existence of a problem, and reading it as an
        // all-clear would be exactly backwards.
        (new Regex(
            @"\b(?:no|none|not\s+aware\s+of\s+any|aren'?t\s+any|isn'?t\s+any)\b" +
            @"(?:\s+\w+){0,3}?\s*" +
            @"\b(?:in)?compatibilit(?:y|ies)\b(?!\s*(?:patch|fix|mod|version|update))|" +
            @"\bno\s+(?:known\s+|reported\s+|current\s+)?(?:conflicts?|issues?|problems?)\b",
            Options),
            Polarity.Clearance, "reports no known incompatibilities"),

        // "This is not a standalone mod" is the opposite claim and has to be settled first, or the
        // rule below reads it as its own reverse. A lookbehind will not do it: the negation and the
        // word are separated by an article, and often by more.
        (new Regex(@"\b(?:not|isn'?t|aren'?t|never)\b(?:\s+\w+){0,2}\s+stand[\s-]?alone\b", Options),
            Polarity.Caution, "says the mod is not standalone"),

        // "It is a standalone mod" - the author saying it touches nothing else.
        (new Regex(@"\bstand[\s-]?alone\b", Options),
            Polarity.Clearance, "describes the mod as standalone"),

        // ── Blockers ─────────────────────────────────────────────────────────────────────

        (new Regex(
            @"\b(?:not|isn'?t|aren'?t|won'?t\s+be|will\s+not\s+be|never)\s+" +
            @"(?:fully\s+|currently\s+|entirely\s+)?compatib\w*", Options),
            Polarity.Blocker, "says something is not compatible"),

        (new Regex(@"\bincompatib\w*", Options),
            Polarity.Blocker, "says something is incompatible"),

        (new Regex(
            @"\b(?:do\s+not|don'?t|do\s+n'?t|never)\s+(?:use|install|run|combine)\b" +
            @"(?:\s+\w+){0,3}?\s+\b(?:with|alongside|together)\b", Options),
            Polarity.Blocker, "warns against using them together"),

        (new Regex(@"\b(?:conflicts?\s+with|clashes?\s+with|breaks?\s+(?:with|when\s+used\s+with))\b", Options),
            Polarity.Blocker, "names a conflict"),

        // ── Conditional - it works, but only if you do something ─────────────────────────

        // A bare "patch" is worthless and was actively misleading. Two ways it went wrong, both
        // seen on real pages:
        //
        //   • "my old patchless version" - patch\w* happily matched the middle of "patchless",
        //     which says the opposite of what was reported.
        //   • "Updated for the v0.11.7 patch" - a *game* patch. Every mod's version history says
        //     this, so the researcher reported a compatibility finding on essentially every mod
        //     that keeps a changelog in its description.
        //
        // So the word only counts when something in the sentence makes it a patch *between mods*:
        // it is called a compatibility patch, it is a patch for or to something, or it is being
        // referred to as a specific object ("the patch below", "a patch exists"). A version number
        // sitting in front of it does none of those.
        (new Regex(
            @"\bcompatibilit\w*\s+patch(?:es)?\b" +
            @"|\bpatch(?:es)?\b\s+(?:for|to|that|which)\b" +
            @"|\b(?:a|the|this|my|his|her|their|no)\s+patch(?:es)?\b" +
            @"|\bpatched\s+version\b", Options),
            Polarity.Caution, "mentions a compatibility patch"),

        // "Load this after Foo" and "load it below the other mod" are the same instruction as
        // "load order", and are how people actually write it - so a couple of words are allowed
        // between the verb and the direction.
        (new Regex(@"\bload\s+(?:\w+\s+){0,2}(?:order|after|before|last|first|below|above)\b", Options),
            Polarity.Caution, "mentions load order"),

        // What a mod replaces is the single most useful thing a description can say about a file
        // conflict - it is the author listing, by name, the things this mod will take over.
        (new Regex(@"\b(?:overwrit\w*|overrid\w*|replac(?:e|es|ed|ing|ement|ements)\b|replacer)\b", Options),
            Polarity.Caution, "mentions overwriting files"),

        // "Requires" used to be kept, and it was pure noise: near enough every mod page says it
        // requires MOMI, which tells the user nothing about the two mods they are looking at. A
        // requirement only matters here when it names one of the other mods in the conflict, and
        // that is decided by ModNameMatcher rather than by a keyword, so nothing is lost by
        // dropping the word.

        // ── Positive statements that are not a blanket all-clear ─────────────────────────

        (new Regex(
            @"\b(?:fully|completely|totally|100%)\s+compatib\w*|" +
            @"\b(?:works?|plays?|runs?)\s+(?:fine\s+|well\s+|perfectly\s+|nicely\s+)?" +
            @"(?:with|alongside|together)\b", Options),
            Polarity.Clearance, "states that they work together"),

        // ── Anything else that discusses the subject at all ──────────────────────────────

        (new Regex(@"\bcompatib\w*", Options),
            Polarity.Context, "mentions compatibility"),

        (new Regex(@"\bconflict\w*", Options),
            Polarity.Context, "mentions a conflict")
    ];

    /// <summary>
    /// Classifies one sentence, or null when it says nothing about compatibility.
    /// </summary>
    public static CompatibilitySignal? Classify(string sentence)
    {
        if (string.IsNullOrWhiteSpace(sentence)) return null;

        foreach (var (pattern, polarity, reason) in Rules)
            if (pattern.IsMatch(sentence))
                return new CompatibilitySignal(polarity, reason);

        return null;
    }

    /// <summary>
    /// The stricter bar used for comment threads and bug reports.
    ///
    /// A description is a few paragraphs written by the author; a comment thread is hundreds of
    /// posts by everyone, most of them "love this mod". A post that merely contains the word
    /// "replace" is noise, so only a stated verdict - a blocker, a clearance, or a patch - earns a
    /// place unless the post also names one of the other mods.
    /// </summary>
    public static CompatibilitySignal? ClassifyDiscussion(string post)
    {
        var signal = Classify(post);
        return signal?.Polarity is Polarity.Context ? null : signal;
    }
}
