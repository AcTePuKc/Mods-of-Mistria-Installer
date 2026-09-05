using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Garethp.ModsOfMistriaInstallerLib.Crash;

/// <summary>What a supervised run proved about one mod.</summary>
public enum CrashTrialVerdict
{
    /// <summary>Nobody has run the game without this mod yet.</summary>
    Untested,

    /// <summary>
    /// The game crashed the same way with this mod switched off, so the mod is not the cause.
    /// This is the verdict that lets AIM stop accusing it and switch it back on.
    /// </summary>
    Cleared,

    /// <summary>
    /// The game ran without crashing once this mod was switched off. Not a proof - a second mod
    /// could need the first to misbehave - but it is the strongest answer a single trial can give.
    /// </summary>
    Guilty,

    /// <summary>
    /// The run answered nothing: the game would not start, the rebuild failed, or the user closed
    /// it before it had got anywhere. Recorded so the candidate is not silently skipped, and
    /// retried rather than believed.
    /// </summary>
    Inconclusive
}

/// <summary>One trial: a mod, the crash it was tested against, and what happened.</summary>
/// <param name="Manual">
/// The user said so, rather than a run proving it.
///
/// Kept as a separate fact from the verdict rather than as two more verdicts, because it answers a
/// different question: the verdict is what is believed about the mod, this is who believes it. Every
/// piece of logic that acts on a verdict - what to test next, what to count as answered, what to put
/// back on - wants the same answer either way, and only the wording the user reads should change.
///
/// It has to change, though. A user who knows this mod is fine because they have seen the same crash
/// without it has evidence AIM cannot check, and AIM should not later present that as something it
/// established itself.
/// </param>
public sealed record CrashTrial(
    string ModId,
    string ModVersion,
    CrashTrialVerdict Verdict,
    DateTimeOffset TestedAt,
    string Note,
    bool Manual = false)
{
    public string Describe() =>
        $"{Verdict switch
        {
            CrashTrialVerdict.Cleared => Manual ? "marked not a culprit" : "ruled out",
            CrashTrialVerdict.Guilty => Manual ? "marked as a crash causer" : "the likely cause",
            CrashTrialVerdict.Inconclusive => "tested, no answer",
            _ => "untested"
        }} ({TestedAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture)})";
}

/// <summary>
/// Remembers what each disable-and-check run proved, so the search for a bad mod is one search
/// rather than the same search started over every time the window opens.
///
/// Finding the mod behind a crash is elimination, and elimination only works if the eliminations
/// are written down. Without this store a user disables the top suspect, runs the game, watches the
/// crash come back, and has learned something real - that the top suspect is innocent - which AIM
/// then throws away, so the next Check Crashes accuses it all over again and the user does the same
/// four-minute run twice.
///
/// A trial is keyed to the crash *and* to the mod's version, for the reason a dismissal is: both
/// halves can change underneath it. A different crash is a different question, and an updated mod
/// is different code, so neither inherits the old verdict.
///
/// It lives beside the profiles, the Nexus index and the dismissed issues in the mods folder rather
/// than inside any mod's own folder, because an update replaces a mod folder wholesale and a record
/// kept there would vanish exactly when it became most misleading to have lost it.
/// </summary>
public sealed class CrashTrialStore
{
    public const string FileName = "aim_crash_trials.json";

    private readonly string _path;
    private readonly Dictionary<string, CrashTrial> _trials = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _recordedAt = new(StringComparer.Ordinal);

    public CrashTrialStore(string modsLocation)
    {
        _path = Path.Combine(modsLocation, FileName);
        Load();
    }

    /// <summary>
    /// The key a trial hangs on. Version is part of it deliberately: clearing a mod says something
    /// about the code that was on disk at the time, and an update replaces that code.
    /// </summary>
    public static string KeyFor(string crashKey, string modId, string? modVersion) =>
        $"{crashKey}|{modId.ToLowerInvariant()}|{modVersion ?? ""}";

    /// <summary>What is known about this mod for this crash. Never null - Untested is an answer.</summary>
    public CrashTrial Trial(string crashKey, string modId, string? modVersion)
    {
        if (string.IsNullOrEmpty(crashKey) || string.IsNullOrEmpty(modId))
            return new CrashTrial(modId, modVersion ?? "", CrashTrialVerdict.Untested, DateTimeOffset.UtcNow, "");

        return _trials.TryGetValue(KeyFor(crashKey, modId, modVersion), out var trial)
            ? trial
            : new CrashTrial(modId, modVersion ?? "", CrashTrialVerdict.Untested, DateTimeOffset.UtcNow, "");
    }

    public CrashTrialVerdict VerdictFor(string crashKey, string modId, string? modVersion) =>
        Trial(crashKey, modId, modVersion).Verdict;

    /// <summary>True when a run has already answered this mod one way or the other.</summary>
    public bool WasTested(string crashKey, string modId, string? modVersion) =>
        VerdictFor(crashKey, modId, modVersion) is CrashTrialVerdict.Cleared or CrashTrialVerdict.Guilty;

    /// <summary>
    /// Records what a run proved - or what the user says - and writes it out immediately.
    /// </summary>
    /// <param name="manual">
    /// True when this is the user's own judgement rather than a run's. Recording
    /// <see cref="CrashTrialVerdict.Untested"/> deletes the entry, which is how a mark is taken back.
    /// </param>
    public void Record(
        string crashKey,
        string modId,
        string? modVersion,
        CrashTrialVerdict verdict,
        string note,
        bool manual = false)
    {
        if (string.IsNullOrEmpty(crashKey) || string.IsNullOrEmpty(modId)) return;

        var key = KeyFor(crashKey, modId, modVersion);

        if (verdict == CrashTrialVerdict.Untested)
        {
            if (_trials.Remove(key)) _recordedAt.Remove(key);
            Save();
            return;
        }

        var now = DateTimeOffset.UtcNow;
        _trials[key] = new CrashTrial(modId, modVersion ?? "", verdict, now, Shorten(note), manual);
        _recordedAt[key] = now;
        Save();
    }

    /// <summary>
    /// The verdict that caught this mod, for any crash, or null if no run ever has.
    ///
    /// This is the question the mod list asks - "should this row say it crashes the game" - and it
    /// is a different question from the crash window's, which is always about one crash. A mod
    /// proven to crash the game is worth a mark on the row whichever crash proved it, because the
    /// user is about to tick it back on without remembering the evening they spent finding it.
    ///
    /// The version has to match. A verdict is about the code that was on disk when the game ran,
    /// and an update is different code - quite possibly the update that fixes exactly this. Marking
    /// a fixed mod as a crasher would be worse than not marking it at all, because it would teach
    /// the user to disbelieve the mark.
    /// </summary>
    public CrashTrial? GuiltyVerdict(string modId, string? modVersion)
    {
        if (string.IsNullOrEmpty(modId)) return null;

        var version = modVersion ?? "";

        return _trials.Values
            .Where(trial =>
                trial.Verdict == CrashTrialVerdict.Guilty &&
                string.Equals(trial.ModId, modId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(trial.ModVersion, version, StringComparison.Ordinal))
            .OrderByDescending(trial => trial.TestedAt)
            .FirstOrDefault();
    }

    /// <summary>How many mods have been answered for this crash, for "3 of 7 ruled out".</summary>
    public int Answered(string crashKey) =>
        _trials.Count(entry =>
            entry.Key.StartsWith(crashKey + "|", StringComparison.Ordinal) &&
            entry.Value.Verdict is CrashTrialVerdict.Cleared or CrashTrialVerdict.Guilty);

    /// <summary>
    /// Throws away every verdict for one crash, so the hunt starts again from nothing.
    ///
    /// Wanted after the user changes something that invalidates the earlier runs - reordering the
    /// list, adding a mod - because a trial is only meaningful against the set it was run in.
    /// </summary>
    public void ForgetCrash(string crashKey)
    {
        if (string.IsNullOrEmpty(crashKey)) return;

        var prefix = crashKey + "|";
        var mine = _trials.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        if (mine.Count == 0) return;

        foreach (var key in mine)
        {
            _trials.Remove(key);
            _recordedAt.Remove(key);
        }

        Save();
    }

    /// <summary>
    /// Drops verdicts old enough that the mods they were about are almost certainly gone.
    ///
    /// Same reasoning as the dismissed-issue store: age is the only signal here that does not
    /// depend on which mods happen to be ticked right now.
    /// </summary>
    public void PruneOlderThan(TimeSpan age)
    {
        var cutoff = DateTimeOffset.UtcNow - age;
        var stale = _recordedAt.Where(entry => entry.Value < cutoff).Select(entry => entry.Key).ToList();
        if (stale.Count == 0) return;

        foreach (var key in stale)
        {
            _trials.Remove(key);
            _recordedAt.Remove(key);
        }

        Save();
    }

    // ── Persistence ──────────────────────────────────────────────────────────────

    private static string Shorten(string note)
    {
        if (string.IsNullOrWhiteSpace(note)) return "";

        var firstLine = note.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return firstLine.Length <= 200 ? firstLine : firstLine[..200] + "…";
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;

            using var reader = new JsonTextReader(new StringReader(File.ReadAllText(_path)))
            {
                DateParseHandling = DateParseHandling.None
            };

            if (JToken.ReadFrom(reader) is not JObject root || root["trials"] is not JObject trials) return;

            foreach (var entry in trials.Properties())
            {
                // One unreadable entry loses that entry, not the file. A user who has spent an
                // evening bisecting forty mods should not lose the lot to one bad timestamp.
                try
                {
                    if (entry.Value is not JObject value) continue;

                    var verdict = Parse(value.Value<string>("verdict"));
                    if (verdict == CrashTrialVerdict.Untested) continue;

                    var when = ReadTimestamp(value);

                    _trials[entry.Name] = new CrashTrial(
                        value.Value<string>("mod") ?? "",
                        value.Value<string>("version") ?? "",
                        verdict,
                        when,
                        value.Value<string>("note") ?? "",
                        value.Value<bool?>("manual") ?? false);

                    _recordedAt[entry.Name] = when;
                }
                catch (Exception exception)
                {
                    Logger.Log($"Skipped an unreadable entry in {FileName}: {exception.Message}");
                }
            }
        }
        catch (Exception exception)
        {
            // A corrupt file must not stop the crash window from opening. Losing the verdicts costs
            // the user some repeated runs; refusing to show the crash costs them the diagnosis.
            Logger.Log($"Could not read {FileName}: {exception.Message}");
            _trials.Clear();
            _recordedAt.Clear();
        }
    }

    private static CrashTrialVerdict Parse(string? text) => text switch
    {
        "cleared" => CrashTrialVerdict.Cleared,
        "guilty" => CrashTrialVerdict.Guilty,
        "inconclusive" => CrashTrialVerdict.Inconclusive,
        _ => CrashTrialVerdict.Untested
    };

    private static string Write(CrashTrialVerdict verdict) => verdict switch
    {
        CrashTrialVerdict.Cleared => "cleared",
        CrashTrialVerdict.Guilty => "guilty",
        CrashTrialVerdict.Inconclusive => "inconclusive",
        _ => "untested"
    };

    private static DateTimeOffset ReadTimestamp(JObject entry) =>
        DateTimeOffset.TryParse(entry.Value<string>("testedAt"), CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var value)
            ? value
            : DateTimeOffset.UtcNow;

    private void Save()
    {
        try
        {
            var trials = new JObject();

            foreach (var (key, trial) in _trials)
                trials[key] = new JObject
                {
                    ["mod"] = trial.ModId,
                    ["version"] = trial.ModVersion,
                    ["verdict"] = Write(trial.Verdict),
                    ["testedAt"] = trial.TestedAt.ToString("o"),
                    ["note"] = trial.Note,
                    ["manual"] = trial.Manual
                };

            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            File.WriteAllText(_path, new JObject { ["trials"] = trials }.ToString(Formatting.Indented));
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not write {FileName}: {exception.Message}");
        }
    }
}
