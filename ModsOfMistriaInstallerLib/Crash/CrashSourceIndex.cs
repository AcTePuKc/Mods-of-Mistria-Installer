using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Garethp.ModsOfMistriaInstallerLib.Crash;

/// <summary>The code a backtrace frame points at, read back out of the installed archive.</summary>
/// <param name="Line">The offending line itself, trimmed.</param>
/// <param name="Context">A few lines either side, for a user who wants to see the shape of it.</param>
/// <param name="Function">The GML function the line sits inside, when one could be found.</param>
/// <param name="DataDomains">
/// The fiddle data sets the enclosing function reads - <c>fiddle_get("stores")</c> yields "stores".
/// This is the bridge from a crash in engine code to the mods that could have caused it: engine
/// code does not change, so when it fails on data it is the data that is wrong, and AIM knows
/// exactly which mods contributed to each data set.
/// </param>
public sealed record CrashSource(
    string Path,
    int Line,
    string Text,
    IReadOnlyList<string> Context,
    string? Function,
    IReadOnlyList<string> DataDomains);

/// <summary>
/// Reads the built game archive to find out what the crashing line actually says.
///
/// This is the step that turns a backtrace into an explanation. "assets/gml/scripts/Stores.gml:16"
/// tells a user nothing; <c>cat.icon = string_to_asset(cat.icon)</c>, inside <c>load_stores()</c>,
/// which reads <c>fiddle_get("stores")</c>, tells them that a store category somewhere has no icon
/// and that the mods to look at are the ones that add store categories.
///
/// The archive is opened read-only and one entry at a time. It is 600MB, but a zip's directory is
/// read without touching the body, and the files wanted here are a few kilobytes of text.
/// </summary>
public sealed class CrashSourceIndex : IDisposable
{
    private readonly ZipArchive? _archive;
    private readonly string? _unpacked;
    // Null values are cached deliberately: "this path is not in the archive" is an answer worth
    // remembering, and a backtrace names the same file on several frames.
    private readonly Dictionary<string, string[]?> _cache = new(StringComparer.OrdinalIgnoreCase);

    private CrashSourceIndex(ZipArchive? archive, string? unpacked)
    {
        _archive = archive;
        _unpacked = unpacked;
    }

    /// <summary>True when there is an archive to read. False is a reason to show less, not to fail.</summary>
    public bool Available => _archive is not null || _unpacked is not null;

    /// <summary>
    /// Opens whichever form of the assets the installation is in.
    ///
    /// An unpacked <c>assets/</c> folder wins when both exist, because that is what the game loads
    /// when it is there - reading the archive in that case would describe code that is not running.
    /// </summary>
    public static CrashSourceIndex Open(string mistriaLocation)
    {
        try
        {
            var unpacked = Path.Combine(mistriaLocation, "assets");
            if (Directory.Exists(unpacked)) return new CrashSourceIndex(null, mistriaLocation);

            var archive = Path.Combine(mistriaLocation, "assets.zip");
            if (!File.Exists(archive)) return new CrashSourceIndex(null, null);

            return new CrashSourceIndex(ZipFile.OpenRead(archive), null);
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not open the game archive to read the crash source: {exception.Message}");
            return new CrashSourceIndex(null, null);
        }
    }

    /// <summary>How many lines either side of the offending one to keep.</summary>
    private const int Around = 6;

    public CrashSource? Read(CrashFrame frame)
    {
        var lines = Lines(frame.Path);
        if (lines is null || frame.Line < 1 || frame.Line > lines.Length) return null;

        var index = frame.Line - 1;
        var from = Math.Max(0, index - Around);
        var to = Math.Min(lines.Length - 1, index + Around);

        var context = new List<string>();
        for (var i = from; i <= to; i++) context.Add($"{i + 1,6}{(i == index ? " >" : "  ")} {lines[i]}");

        var (function, start) = EnclosingFunction(lines, index);

        return new CrashSource(
            frame.Path,
            frame.Line,
            lines[index].Trim(),
            context,
            function,
            DomainsIn(lines, start, index));
    }

    /// <summary>
    /// Any text file in the built archive, by its archive-relative path.
    ///
    /// Used to look at the merged data the game actually loaded, which is the difference between
    /// "one of these mods might have done it" and "the file the game read has the fault in it, on
    /// line 412, and here is which mod put it there".
    /// </summary>
    public IReadOnlyList<string>? ReadAll(string path) => Lines(path);

    private string[]? Lines(string path)
    {
        if (_cache.TryGetValue(path, out var cached)) return cached;

        try
        {
            string? text = null;

            if (_unpacked is not null)
            {
                var full = Path.Combine(_unpacked, path.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(full)) text = File.ReadAllText(full);
            }
            else if (_archive is not null)
            {
                // Zip entries are ordinal-cased, and the runtime prints the path it was given, so
                // an exact hit is the normal case and the scan is the fallback for a mismatch.
                var entry = _archive.GetEntry(path) ??
                            _archive.Entries.FirstOrDefault(candidate =>
                                string.Equals(candidate.FullName, path, StringComparison.OrdinalIgnoreCase));

                if (entry is not null)
                {
                    using var stream = entry.Open();
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    text = reader.ReadToEnd();
                }
            }

            if (text is null) return _cache[path] = null;

            return _cache[path] = text.Replace("\r\n", "\n").Split('\n');
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not read {path} from the game archive: {exception.Message}");
            return _cache[path] = null;
        }
    }

    private static readonly Regex FunctionHead =
        new(@"^\s*(?:static\s+)?(?:function\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)|(?<name2>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*function)\s*\(",
            RegexOptions.Compiled);

    /// <summary>
    /// The nearest function header at or above the offending line.
    ///
    /// Walking backwards rather than parsing: GML has no import structure to follow and a crash
    /// report is not worth a parser. The first header above the line is right except inside a
    /// nested closure, where it names the closure - which is still the more useful of the two.
    /// </summary>
    private static (string? Name, int Start) EnclosingFunction(string[] lines, int index)
    {
        for (var i = index; i >= 0; i--)
        {
            var match = FunctionHead.Match(lines[i]);
            if (!match.Success) continue;

            var name = match.Groups["name"].Success ? match.Groups["name"].Value : match.Groups["name2"].Value;
            return (name, i);
        }

        return (null, 0);
    }

    private static readonly Regex FiddleRead =
        new(@"fiddle_get\s*\(\s*[""'](?<name>[A-Za-z0-9_/\-]+)[""']", RegexOptions.Compiled);

    /// <summary>
    /// Which fiddle data sets the enclosing function reads, searched from its header down to a
    /// little past the crash - not the whole file, because a 300-line script may touch six of them
    /// and only the ones this function reads are evidence.
    /// </summary>
    private static IReadOnlyList<string> DomainsIn(string[] lines, int start, int index)
    {
        var found = new List<string>();
        var to = Math.Min(lines.Length - 1, index + Around);

        for (var i = start; i <= to; i++)
            foreach (Match match in FiddleRead.Matches(lines[i]))
            {
                var name = match.Groups["name"].Value;
                if (!found.Contains(name, StringComparer.OrdinalIgnoreCase)) found.Add(name);
            }

        return found;
    }

    public void Dispose() => _archive?.Dispose();
}
