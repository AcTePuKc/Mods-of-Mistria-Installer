using System.Text;
using Garethp.ModsOfMistriaInstallerLib.ModTypes;

namespace Garethp.ModsOfMistriaInstallerLib.Crash;

/// <summary>A bug report ready to paste, and the page to paste it into.</summary>
/// <param name="Url">
/// The mod's bug tracker, when AIM knows where the mod came from. AIM opens the page; the user
/// writes nothing and submits it themselves, because a report filed in somebody's name without
/// them reading it is not a report, it is spam.
/// </param>
public sealed record ComposedReport(string Title, string Body, string? Url);

/// <summary>
/// Writes the bug report the user would otherwise have to write by hand.
///
/// Every mod author asking for a bug report asks for the same four things - what happened, what the
/// game said, what else was installed, and how to reproduce it - and every user filing one has all
/// four in front of them and no idea that is what is wanted. So AIM writes the report: the exact
/// error and backtrace, the game version, the mod and its version, the other mods that touch the
/// same part of the game, and what AIM itself concluded and why.
///
/// It stops short of submitting. The text is the user's to read, edit and post under their own
/// name, and a report they have not read is worse for the author than no report at all.
/// </summary>
public static class CrashReportComposer
{
    private const string Game = "fieldsofmistria";

    /// <summary>The bug tracker for a mod, when its Nexus id is known.</summary>
    public static string? BugTracker(int? modId, string? pageUrl)
    {
        if (modId is not null) return $"https://www.nexusmods.com/{Game}/mods/{modId}?tab=bugs";

        return pageUrl is null ? null : pageUrl.TrimEnd('/') + "?tab=bugs";
    }

    /// <summary>
    /// The report for one suspect.
    /// </summary>
    /// <param name="others">
    /// Other installed mods that touch the same part of the game. Named because the first thing
    /// any author asks is "what else do you have installed", and because it is often the answer:
    /// a mod that works alone and breaks beside one other is the author's business to know about.
    /// </param>
    public static ComposedReport ForSuspect(
        CrashSuspect suspect,
        GameCrashLog crash,
        CrashDiagnosis diagnosis,
        IMod? mod,
        IReadOnlyList<string> others,
        int? nexusModId,
        string? pageUrl)
    {
        var text = new StringBuilder();

        text.AppendLine("**What happened**");
        text.AppendLine();
        text.AppendLine(diagnosis.Stale
            ? "The game crashed. Note that I have since changed which mods are installed, so this " +
              "report describes the set-up at the time of the crash rather than my current one."
            : "The game crashed while loading, with the error below.");
        text.AppendLine();

        text.AppendLine("**The error**");
        text.AppendLine();
        text.AppendLine("```");
        text.AppendLine(crash.Tidied);

        if (crash.Frames.Count > 0)
        {
            text.AppendLine("VM backtrace:");
            foreach (var frame in crash.Frames) text.AppendLine("   " + frame);
        }

        text.AppendLine("```");
        text.AppendLine();

        var offending = diagnosis.Sources.FirstOrDefault();

        if (offending is not null)
        {
            text.AppendLine("**The line it stopped on**");
            text.AppendLine();
            text.AppendLine("```");
            if (offending.Function is not null) text.AppendLine($"in {offending.Function}()");
            foreach (var line in offending.Context) text.AppendLine(line);
            text.AppendLine("```");
            text.AppendLine();
        }

        text.AppendLine("**Why I think it is this mod**");
        text.AppendLine();
        foreach (var reason in suspect.Evidence) text.AppendLine("- " + reason);
        text.AppendLine();

        text.AppendLine("**Set-up**");
        text.AppendLine();
        text.AppendLine($"- Mod: {suspect.Name}{(mod is null ? "" : " " + mod.GetVersion())}");
        text.AppendLine($"- Game version: {Context(crash, "app.app_version")}");
        text.AppendLine($"- Installed with AIM {Context(crash, "aim.installerVersion")}");
        text.AppendLine($"- Crash happened: {crash.When.LocalDateTime:g}");
        text.AppendLine($"- In game at the time: {Context(crash, "game_state.in_game")}");

        if (others.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Other installed mods that change the same part of the game:");
            foreach (var other in others.Take(20)) text.AppendLine("- " + other);
            if (others.Count > 20) text.AppendLine($"- …and {others.Count - 20} more");
        }

        text.AppendLine();
        text.AppendLine("**Steps to reproduce**");
        text.AppendLine();
        text.AppendLine("1. Install the mods listed above.");
        text.AppendLine("2. Launch the game.");
        text.AppendLine(crash.Context.TryGetValue("game_state.in_game", out var inGame) &&
                        string.Equals(inGame, "True", StringComparison.OrdinalIgnoreCase)
            ? "3. Load a save. The game stops with the error above."
            : "3. The game stops with the error above before reaching the title screen.");
        text.AppendLine();
        text.AppendLine("_(Report drafted by AIM from the game's own crash log. " +
                        "Please tell me if you need anything else from my install.)_");

        return new ComposedReport(
            Title(suspect, crash),
            text.ToString(),
            BugTracker(nexusModId, pageUrl));
    }

    /// <summary>
    /// A title an author can triage from the bug list without opening it: what broke, and where.
    /// </summary>
    private static string Title(CrashSuspect suspect, GameCrashLog crash)
    {
        var where = suspect.Where is null ? "" : $" ({suspect.Where})";

        return crash.Symptom switch
        {
            CrashSymptom.MissingField =>
                $"Crash on load: no such field \"{crash.Subject}\"{where}",
            CrashSymptom.UndefinedVariable =>
                $"Crash on load: {crash.Subject} read before it is set{where}",
            CrashSymptom.NotAFunction =>
                $"Crash on load: call to something that is not a function{where}",
            CrashSymptom.MissingAsset =>
                $"Crash on load: asset \"{crash.Subject}\" does not exist{where}",
            _ => $"Crash: {Shorten(crash.Tidied)}"
        };
    }

    /// <summary>
    /// The whole thing as one block of text, for the clipboard.
    ///
    /// Used by the copy button and by the "no mod page to post to" case, where the user's options
    /// are the author's Discord, a forum, or a comment - all of which take pasted text and none of
    /// which AIM can post to.
    /// </summary>
    public static string ForClipboard(GameCrashLog crash, CrashDiagnosis diagnosis)
    {
        var text = new StringBuilder();

        text.AppendLine($"AIM crash report — {crash.When.LocalDateTime:g}");
        text.AppendLine();
        text.AppendLine(diagnosis.Headline);
        text.AppendLine();

        foreach (var reason in diagnosis.Reasons) text.AppendLine("• " + reason);

        text.AppendLine();
        text.AppendLine(crash.Tidied);

        foreach (var frame in crash.Frames) text.AppendLine("   " + frame);

        if (diagnosis.Suspects.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Mods this points at:");

            foreach (var suspect in diagnosis.Suspects)
            {
                text.AppendLine($"  {suspect.Name} — {suspect.Confidence}");
                foreach (var evidence in suspect.Evidence) text.AppendLine("     " + evidence);
            }
        }

        if (diagnosis.ModsAtLaunch.Count > 0)
        {
            text.AppendLine();
            text.AppendLine($"Load order at the time ({diagnosis.ModsAtLaunch.Count} mods):");
            foreach (var mod in diagnosis.ModsAtLaunch) text.AppendLine("  " + mod);
        }

        foreach (var (key, value) in crash.Context.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            text.AppendLine($"{key} = {value}");

        return text.ToString();
    }

    private static string Context(GameCrashLog crash, string key) =>
        crash.Context.TryGetValue(key, out var value) && value.Length > 0 ? value : "unknown";

    private static string Shorten(string text) =>
        text.Length <= 70 ? text : text[..67].TrimEnd() + "…";
}
