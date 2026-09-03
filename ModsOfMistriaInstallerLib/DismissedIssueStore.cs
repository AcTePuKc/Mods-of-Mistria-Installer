using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Garethp.ModsOfMistriaInstallerLib;

/// <summary>What the user concluded about an issue, and where they found it out.</summary>
/// <param name="Kind">
/// One of <c>not-an-issue</c>, <c>patch</c>, <c>incompatible</c>, <c>rebound</c>, or empty when the
/// user simply ticked the box without saying why.
/// </param>
/// <param name="Link">Where the answer came from - usually a compatibility patch.</param>
/// <param name="Note">Any detail worth keeping, such as which mod was rebound and to what.</param>
public sealed record IssueVerdict(string Kind, string? Link = null, string? Note = null);

/// <summary>
/// Remembers the issues the user has looked at and decided are fine.
///
/// AIM's conflict checks are deliberately conservative: two mods writing the same sprite is
/// reported because it *might* matter, not because it does. A user who has already worked out that
/// a given pair is harmless should not have to re-read it every time they open the report, or the
/// report stops being read at all.
///
/// A dismissal is keyed to the mods and the versions involved (see <see cref="LoadOrderNote.StableKey"/>),
/// so updating either mod brings the issue back for a fresh judgement - the old "I checked this"
/// was about code that is no longer installed.
///
/// It lives beside the profiles and the Nexus index in the mods folder rather than in per-mod
/// folders, for the same reason those do: a mod folder is replaced wholesale by an update.
/// </summary>
public sealed class DismissedIssueStore
{
    public const string FileName = "aim_dismissed_issues.json";

    public const string VerdictNotAnIssue = "not-an-issue";
    public const string VerdictPatchExists = "patch";
    public const string VerdictIncompatible = "incompatible";
    public const string VerdictRebound = "rebound";

    private readonly string _path;

    // When each judgement was made. Every key the store knows about has an entry here, whether it
    // was dismissed, given a verdict, or both - so pruning has one timestamp to work from and a
    // verdict-only entry cannot outlive a dismissed one.
    private readonly Dictionary<string, DateTimeOffset> _recordedAt = new(StringComparer.Ordinal);
    private readonly HashSet<string> _dismissed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _labels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IssueVerdict> _verdicts = new(StringComparer.Ordinal);

    public DismissedIssueStore(string modsLocation)
    {
        _path = Path.Combine(modsLocation, FileName);
        Load();
    }

    public int Count => _dismissed.Count;

    public bool IsDismissed(string key) => key.Length > 0 && _dismissed.Contains(key);

    /// <summary>What the user concluded, when they said. Null when they never recorded a reason.</summary>
    public IssueVerdict? Verdict(string key) =>
        key.Length > 0 && _verdicts.TryGetValue(key, out var verdict) ? verdict : null;

    /// <summary>Records the user's judgement and writes it out immediately.</summary>
    public void SetDismissed(string key, bool dismissed, string? label = null, IssueVerdict? verdict = null)
    {
        if (string.IsNullOrEmpty(key)) return;

        if (dismissed)
        {
            _dismissed.Add(key);
            _recordedAt[key] = DateTimeOffset.UtcNow;
            if (!string.IsNullOrWhiteSpace(label)) _labels[key] = Shorten(label);
            if (verdict is not null) _verdicts[key] = verdict;
        }
        else
        {
            // Un-ticking the box withdraws the conclusion too. Keeping "a patch exists" attached to
            // an issue the user has reopened would put a stale answer beside a live question.
            Forget(key);
        }

        Save();
    }

    /// <summary>
    /// Records a conclusion that does not resolve the issue - "these two really are incompatible" -
    /// so the finding survives without the issue being hidden.
    /// </summary>
    public void SetVerdict(string key, IssueVerdict? verdict)
    {
        if (string.IsNullOrEmpty(key)) return;

        if (verdict is null)
        {
            _verdicts.Remove(key);
            if (!_dismissed.Contains(key)) Forget(key);
        }
        else
        {
            _verdicts[key] = verdict;
            _recordedAt[key] = DateTimeOffset.UtcNow;
        }

        Save();
    }

    private void Forget(string key)
    {
        _dismissed.Remove(key);
        _labels.Remove(key);
        _verdicts.Remove(key);
        _recordedAt.Remove(key);
    }

    /// <summary>
    /// Drops judgements old enough that the mods they were made about are almost certainly gone.
    ///
    /// Pruning against "the issues in the report I am opening right now" would be wrong: the report
    /// only covers the mods that are currently ticked, so disabling a mod, opening the report and
    /// re-enabling it would silently throw away every judgement the user had made about it. Age is
    /// the only signal here that does not depend on what happens to be selected.
    /// </summary>
    public void PruneOlderThan(TimeSpan age)
    {
        var cutoff = DateTimeOffset.UtcNow - age;
        var stale = _recordedAt.Where(entry => entry.Value < cutoff).Select(entry => entry.Key).ToList();
        if (stale.Count == 0) return;

        foreach (var key in stale) Forget(key);

        Save();
    }

    // ── Persistence ──────────────────────────────────────────────────────────────

    // The label is stored purely so the file is readable by a human wondering what they silenced.
    // Nothing reads it back.
    private static string Shorten(string label)
    {
        var firstLine = label.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return firstLine.Length <= 160 ? firstLine : firstLine[..160] + "…";
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;

            if (ReadRoot(_path)?["issues"] is not JObject issues) return;

            foreach (var entry in issues.Properties())
            {
                // One malformed entry loses that entry, not the file. Wiping every judgement the
                // user has ever made because a single timestamp is unreadable would be a poor
                // trade for the tidier code.
                try
                {
                    if (entry.Value is not JObject value) continue;

                    // A verdict can exist without a dismissal ("these really are incompatible"), so
                    // the two are read independently rather than one implying the other. Files
                    // written before verdicts existed have no "dismissed" field and were all
                    // dismissals.
                    _recordedAt[entry.Name] = ReadTimestamp(value, "dismissedAt");
                    if (value.Value<bool?>("dismissed") ?? true) _dismissed.Add(entry.Name);

                    var label = value.Value<string>("note");
                    if (!string.IsNullOrWhiteSpace(label)) _labels[entry.Name] = label;

                    var kind = value.Value<string>("verdict");
                    if (!string.IsNullOrWhiteSpace(kind))
                        _verdicts[entry.Name] = new IssueVerdict(
                            kind, value.Value<string>("link"), value.Value<string>("verdictNote"));
                }
                catch (Exception exception)
                {
                    Logger.Log($"Skipped an unreadable entry in {FileName}: {exception.Message}");
                }
            }
        }
        catch (Exception exception)
        {
            // A corrupt file must not stop the report from opening. Losing the dismissals is
            // annoying; refusing to show the conflicts is worse.
            Logger.Log($"Could not read {FileName}: {exception.Message}");
            _dismissed.Clear();
            _labels.Clear();
            _verdicts.Clear();
            _recordedAt.Clear();
        }
    }

    /// <summary>
    /// Parses the file with timestamps left as text.
    ///
    /// By default Newtonsoft turns an ISO-8601 string into a Date token holding a
    /// <see cref="DateTime"/>, which loses the offset and makes the value's string form depend on
    /// Newtonsoft's own settings. Leaving it alone means <see cref="ReadTimestamp"/> sees exactly
    /// what was written.
    /// </summary>
    private static JObject? ReadRoot(string path)
    {
        using var reader = new JsonTextReader(new StringReader(File.ReadAllText(path)))
        {
            DateParseHandling = DateParseHandling.None
        };

        return JToken.ReadFrom(reader) as JObject;
    }

    /// <summary>
    /// Reads an ISO-8601 stamp as text and parses it here.
    ///
    /// Not <c>Value&lt;DateTimeOffset?&gt;</c>: that throws when Newtonsoft has already turned the
    /// string into a Date token. The same approach as <c>NexusInstallIndex</c>, which is where this
    /// file's format came from.
    /// </summary>
    private static DateTimeOffset ReadTimestamp(JObject entry, string field)
    {
        var raw = entry.Value<string>(field);

        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var value)
            ? value
            : DateTimeOffset.UtcNow;
    }

    private void Save()
    {
        try
        {
            var issues = new JObject();
            foreach (var (key, recordedAt) in _recordedAt)
            {
                // The timestamp is the one the judgement was made at, never "now" - rewriting it on
                // every save would make a judgement immortal, since pruning goes by age.
                var entry = new JObject
                {
                    ["dismissed"] = _dismissed.Contains(key),
                    ["dismissedAt"] = recordedAt.ToString("o")
                };

                if (_labels.TryGetValue(key, out var label)) entry["note"] = label;
                if (_verdicts.TryGetValue(key, out var verdict))
                {
                    entry["verdict"] = verdict.Kind;
                    if (!string.IsNullOrWhiteSpace(verdict.Link)) entry["link"] = verdict.Link;
                    if (!string.IsNullOrWhiteSpace(verdict.Note)) entry["verdictNote"] = verdict.Note;
                }

                issues[key] = entry;
            }

            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            File.WriteAllText(_path, new JObject { ["issues"] = issues }.ToString(Formatting.Indented));
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not write {FileName}: {exception.Message}");
        }
    }
}
