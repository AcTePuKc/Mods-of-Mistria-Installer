using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Garethp.ModsOfMistriaInstallerLib.Crash;

/// <summary>What kind of failure the runtime reported, in the terms a fix is chosen by.</summary>
public enum CrashSymptom
{
    /// <summary>The message did not match anything AIM knows how to classify.</summary>
    Unknown,

    /// <summary>A struct was missing a field the engine required. Almost always mod data.</summary>
    MissingField,

    /// <summary>A variable was read before anything wrote it. Usually mod code.</summary>
    UndefinedVariable,

    /// <summary>An array or list was indexed past its end.</summary>
    IndexOutOfRange,

    /// <summary>Something that is not a function was called. Usually a hook that did not install.</summary>
    NotAFunction,

    /// <summary>A value could not be converted - a string where a number was wanted, and so on.</summary>
    BadConversion,

    /// <summary>A named asset (sprite, sound, room) did not resolve.</summary>
    MissingAsset
}

/// <summary>
/// One line of the VM backtrace.
/// </summary>
/// <param name="Index">0 is where it actually broke; higher numbers are its callers.</param>
/// <param name="Path">Archive-relative, as the runtime prints it: <c>assets/gml/scripts/Stores.gml</c>.</param>
/// <param name="Note">The runtime's own annotation on this frame, when it wrote one.</param>
public sealed record CrashFrame(int Index, string Path, int Line, string? Note)
{
    /// <summary>True for a file AIM installed on a mod's behalf: <c>assets/gml/scripts/&lt;symbol&gt;/…</c>.</summary>
    public bool IsModCode =>
        Path.StartsWith("assets/gml/scripts/", StringComparison.OrdinalIgnoreCase) &&
        Path.AsSpan("assets/gml/scripts/".Length).IndexOf('/') >= 0;

    /// <summary>
    /// The install namespace this frame sits in, or null for an engine file.
    ///
    /// This is the whole reason a crash can be attributed at all: every mod's GML is installed
    /// under a directory named after its own symbol, so a frame inside one names its mod outright
    /// rather than by inference. See <c>GmlLayer.Stage</c>.
    /// </summary>
    public string? Symbol
    {
        get
        {
            if (!IsModCode) return null;

            var rest = Path["assets/gml/scripts/".Length..];
            var slash = rest.IndexOf('/');
            return slash <= 0 ? null : rest[..slash];
        }
    }

    public override string ToString() =>
        Note is null ? $"{Index}: {Path}:{Line}" : $"{Index}: {Path}:{Line}: {Note}";
}

/// <summary>
/// One crash the game recorded, parsed into the parts an answer can be built from.
///
/// Fields of Mistria writes <c>error_log.json</c> into its own data folder and overwrites it on the
/// next crash, so the file is a "most recent" rather than a history - which is why
/// <see cref="GameRunRecorder"/> copies each one aside as it appears. The interesting half is the
/// <c>report</c> string: a message, then a VM backtrace of archive-relative paths and line numbers.
/// Those paths are what makes attribution possible, because AIM built the archive they point into.
/// </summary>
public sealed record GameCrashLog(
    string Message,
    IReadOnlyList<CrashFrame> Frames,
    DateTimeOffset When,
    string SourcePath,
    string RawReport)
{
    /// <summary>The runtime's own context block, flattened: <c>app.app_version</c>, and so on.</summary>
    public IReadOnlyDictionary<string, string> Context { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The load order in force when this crash was captured, if AIM was the one who launched
    /// the game. Null for a crash found lying in the game's folder, which AIM cannot date to a
    /// particular set of mods.
    /// </summary>
    public IReadOnlyList<string>? ModsAtLaunch { get; init; }

    public CrashSymptom Symptom { get; init; } = CrashSymptom.Unknown;

    /// <summary>
    /// The name at the centre of the failure - the missing field, the unset variable, the
    /// unresolved asset. Empty when the message named nothing.
    ///
    /// This is what turns "some mod broke the stores" into a check AIM can actually run: a field
    /// name can be looked for in the data files of every mod that writes that part of the game.
    /// </summary>
    public string Subject { get; init; } = "";

    /// <summary>Where it broke, as opposed to who called it.</summary>
    public CrashFrame? Innermost => Frames.Count == 0 ? null : Frames[0];

    /// <summary>
    /// Stable across repeats of the same crash and different across different ones, so AIM can say
    /// "this is the same crash as before" and so a dismissal has something to hang on.
    ///
    /// Built from the message with addresses stripped - the object address in a GameMaker message
    /// changes on every run and would otherwise make every occurrence look new - plus the frames.
    /// </summary>
    public string StableKey
    {
        get
        {
            var text = new StringBuilder(Redact(Message));
            foreach (var frame in Frames) text.Append('|').Append(frame.Path).Append(':').Append(frame.Line);

            return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..16];
        }
    }

    /// <summary>The message with run-to-run noise taken out, for display and for comparison.</summary>
    public string Tidied => Redact(Message);

    private static readonly Regex Address = new(@"0x[0-9a-fA-F]+", RegexOptions.Compiled);

    private static string Redact(string text) => Address.Replace(text, "0x…");

    // ── Reading one ──────────────────────────────────────────────────────────────

    private static readonly Regex FrameLine =
        new(@"^\s*(?<i>\d+):\s+(?<path>\S.*?):(?<line>\d+)\s*(?::\s*(?<note>.+?))?\s*$",
            RegexOptions.Compiled);

    /// <summary>
    /// Parses the game's crash file.
    ///
    /// Deliberately forgiving. The file is written by somebody else's program and its shape is
    /// theirs to change; a crash report AIM cannot parse perfectly is still worth showing, so an
    /// unrecognised document falls back to treating the whole text as the message rather than
    /// throwing. The one thing that is refused is an empty file, because there is nothing to say
    /// about it.
    /// </summary>
    public static GameCrashLog? Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;

            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text)) return null;

            var when = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);

            return Parse(text, path, when);
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not read the crash log at {path}: {exception.Message}");
            return null;
        }
    }

    /// <summary>Split out from <see cref="Read"/> so the parsing can be tested without a file.</summary>
    public static GameCrashLog Parse(string text, string sourcePath, DateTimeOffset when)
    {
        var report = text;
        var context = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<string>? mods = null;

        try
        {
            if (JToken.Parse(text) is JObject root)
            {
                report = root.Value<string>("report") ?? text;

                if (root["context"] is JObject block) Flatten(block, "", context);

                // AIM's own capture writes this; the game's does not.
                if (root["aim"] is JObject aim)
                {
                    // When the game crashed, not when AIM copied the file aside. The caller can
                    // only offer the copy's timestamp, and for a capture those are different dates
                    // - which matters, because "is this crash newer than that one" is how AIM
                    // decides whether a supervised run crashed at all. Reading it back from the
                    // file's write time made a capture of last week's crash look like a crash that
                    // had just happened, and a check that proved a mod guilty reported it cleared.
                    if (DateTimeOffset.TryParse(
                            aim.Value<string>("crashedAt"), CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind, out var crashedAt))
                        when = crashedAt;

                    if (aim["mods"] is JArray list)
                        mods = list.Select(entry => entry.ToString()).ToList();

                    foreach (var property in aim.Properties())
                        if (property.Value.Type is not JTokenType.Array and not JTokenType.Object)
                            context["aim." + property.Name] = property.Value.ToString();
                }
            }
        }
        catch
        {
            // Not JSON, or not the JSON expected. The raw text is still a crash report and the
            // backtrace parser below reads it perfectly well; there is nothing to warn about.
        }

        var lines = report.Replace("\r\n", "\n").Split('\n');
        var frames = new List<CrashFrame>();
        var message = new StringBuilder();
        var inBacktrace = false;

        foreach (var line in lines)
        {
            if (line.Contains("backtrace", StringComparison.OrdinalIgnoreCase) && line.TrimEnd().EndsWith(':'))
            {
                inBacktrace = true;
                continue;
            }

            var match = FrameLine.Match(line);

            if (match.Success && (inBacktrace || frames.Count > 0))
            {
                frames.Add(new CrashFrame(
                    int.Parse(match.Groups["i"].Value, CultureInfo.InvariantCulture),
                    match.Groups["path"].Value.Replace('\\', '/').Trim(),
                    int.Parse(match.Groups["line"].Value, CultureInfo.InvariantCulture),
                    match.Groups["note"].Success ? match.Groups["note"].Value.Trim() : null));
                continue;
            }

            if (!inBacktrace && line.Trim().Length > 0)
            {
                if (message.Length > 0) message.Append(' ');
                message.Append(line.Trim());
            }
        }

        var headline = message.Length > 0 ? message.ToString() : report.Trim();
        var (symptom, subject) = Classify(headline);

        return new GameCrashLog(headline, frames, when, sourcePath, report)
        {
            Context = context,
            ModsAtLaunch = mods,
            Symptom = symptom,
            Subject = subject
        };
    }

    private static void Flatten(JObject node, string prefix, IDictionary<string, string> into)
    {
        foreach (var property in node.Properties())
        {
            var key = prefix.Length == 0 ? property.Name : prefix + "." + property.Name;

            // The settings block is the player's entire configuration, key bindings included. It is
            // several hundred values, none of which explains a crash, and putting it in front of a
            // user - or into a bug report they are about to post in public - helps nobody.
            if (string.Equals(key, "settings", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var wanted in InterestingSettings)
                    if (property.Value is JObject settings && settings[wanted] is { } value &&
                        value.Type is not JTokenType.Array and not JTokenType.Object)
                        into["settings." + wanted] = value.ToString();

                continue;
            }

            if (property.Value is JObject child) Flatten(child, key, into);
            else if (property.Value.Type is not JTokenType.Array) into[key] = property.Value.ToString();
        }
    }

    /// <summary>The handful of settings that have ever been the reason for a crash.</summary>
    private static readonly string[] InterestingSettings =
        ["language", "low_spec_mode", "frame_rate_cap", "vsync", "touch_screen"];

    // ── What kind of failure this is ─────────────────────────────────────────────

    private static readonly (CrashSymptom Symptom, Regex Pattern)[] Symptoms =
    [
        (CrashSymptom.MissingField, new Regex(@"no such field\s+[`""']*""?(?<name>[^""`'\s]+)""?", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        (CrashSymptom.UndefinedVariable, new Regex(@"variable\s+(?<name>[A-Za-z_][A-Za-z0-9_.]*)\s+(?:not set|is undefined|has not been set)", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        (CrashSymptom.UndefinedVariable, new Regex(@"unknown\s+variable\s+[`""']?(?<name>[A-Za-z_][A-Za-z0-9_.]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        (CrashSymptom.NotAFunction, new Regex(@"(?:is not a function|cannot call|not callable)\D*[`""']?(?<name>[A-Za-z_][A-Za-z0-9_.]*)?", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        (CrashSymptom.IndexOutOfRange, new Regex(@"(?:index .*out of range|out of bounds|array index)", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        (CrashSymptom.MissingAsset, new Regex(@"(?:asset|sprite|sound|room|object)\s+[`""']?(?<name>[A-Za-z_][A-Za-z0-9_]*)[`""']?\s+(?:does not exist|not found|is undefined)", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        (CrashSymptom.BadConversion, new Regex(@"(?:unable to convert|cannot convert|invalid conversion)", RegexOptions.IgnoreCase | RegexOptions.Compiled))
    ];

    private static (CrashSymptom, string) Classify(string message)
    {
        foreach (var (symptom, pattern) in Symptoms)
        {
            var match = pattern.Match(message);
            if (!match.Success) continue;

            var name = match.Groups["name"].Success ? match.Groups["name"].Value.Trim('"', '`', '\'') : "";
            return (symptom, name);
        }

        return (CrashSymptom.Unknown, "");
    }
}
