using System.Text;
using Garethp.ModsOfMistriaInstallerLib.Seam;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Garethp.ModsOfMistriaInstallerLib.Operations;

public enum SeamRegionStatus
{
    Unchanged,
    Changed,
    OldMissing,
    NewMissing,
}

// One catalog entry's surrounding engine region compared across two pristine builds.
public record SeamRegionDiff(
    string EntryId,
    string Kind,
    string File,
    string Region,
    SeamRegionStatus Status,
    string Note,
    IReadOnlyList<string> DiffLines,
    string OldText,
    string NewText,
    int RegionStartLine = 0,
    int RegionLines = 0);

// Read-only game-update triage. A changed region is review material; it does
// not assert that staging will fail. Catalog markers mean one input is modded
// and therefore not safe to compare as a pristine build.
public class SeamDiffResult(IReadOnlyList<SeamRegionDiff> entries, IReadOnlyList<string> moddedMarkers)
{
    public IReadOnlyList<SeamRegionDiff> Entries { get; } = entries;
    public IReadOnlyList<string> ModdedMarkers { get; } = moddedMarkers;
    public int ChangedCount => Entries.Count(entry => entry.Status == SeamRegionStatus.Changed);
    public int MissingCount => Entries.Count(entry =>
        entry.Status is SeamRegionStatus.OldMissing or SeamRegionStatus.NewMissing);
    public bool Ok => ModdedMarkers.Count == 0 && ChangedCount == 0 && MissingCount == 0;
    public int ExitCode => Ok ? 0 : 1;
}

public static class SeamDiffer
{
    private const int TopLevelContextLines = 15;
    private const int CarryTextLimit = 120;
    private static readonly UTF8Encoding Utf8Strict = new(false, true);

    public static SeamDiffResult Diff(IPristineSource oldPristine, IPristineSource newPristine,
        SeamCatalog? catalog = null)
    {
        if (catalog is null)
        {
            var (name, bytes) = PayloadResolver.SeamCatalog();
            catalog = SeamCatalogLoader.Load(bytes, name);
        }

        Dictionary<string, string?> oldFiles = [];
        Dictionary<string, string?> newFiles = [];
        List<SeamRegionDiff> entries = [];
        HashSet<string> markers = new(StringComparer.Ordinal);

        foreach (var entry in catalog.Entries)
        {
            var oldText = LoadFile(oldPristine, entry.File, oldFiles);
            var newText = LoadFile(newPristine, entry.File, newFiles);

            if (oldText?.Contains(entry.Marker, StringComparison.Ordinal) == true)
                markers.Add($"'{entry.Marker}' ({entry.Id}) in old {entry.File}");
            if (newText?.Contains(entry.Marker, StringComparison.Ordinal) == true)
                markers.Add($"'{entry.Marker}' ({entry.Id}) in new {entry.File}");

            var oldRegion = oldText is null
                ? new Region(null, "", "file not in the old archive")
                : ExtractRegion(entry, oldText);
            var newRegion = newText is null
                ? new Region(null, "", "file not in the new archive")
                : ExtractRegion(entry, newText);
            var kind = entry.Kind.CatalogName();

            if (newRegion.Text is null)
            {
                entries.Add(new SeamRegionDiff(entry.Id, kind, entry.File, oldRegion.Description,
                    SeamRegionStatus.NewMissing, newRegion.Note, [], "", ""));
                continue;
            }

            if (oldRegion.Text is null)
            {
                entries.Add(new SeamRegionDiff(entry.Id, kind, entry.File, newRegion.Description,
                    SeamRegionStatus.OldMissing, oldRegion.Note, [], "", ""));
                continue;
            }

            if (TokenForm(oldRegion.Text) == TokenForm(newRegion.Text))
            {
                entries.Add(new SeamRegionDiff(entry.Id, kind, entry.File, newRegion.Description,
                    SeamRegionStatus.Unchanged, "", [], "", ""));
                continue;
            }

            var oldLines = LineCount(oldRegion.Text);
            var newLines = LineCount(newRegion.Text);
            var carryTexts = oldLines <= CarryTextLimit && newLines <= CarryTextLimit;
            entries.Add(new SeamRegionDiff(entry.Id, kind, entry.File, newRegion.Description,
                SeamRegionStatus.Changed, "", DiffLines(oldRegion.Text, newRegion.Text),
                carryTexts ? oldRegion.Text : "", carryTexts ? newRegion.Text : "",
                newRegion.StartLine, newLines));
        }

        return new SeamDiffResult(entries, markers.Order(StringComparer.Ordinal).ToList());
    }

    private record Region(string? Text, string Description, string Note, int StartLine = 0);

    private static Region ExtractRegion(SeamEntry entry, string text)
    {
        if (entry.TargetFn.Length > 0)
        {
            var spans = GmlScanner.FindFunctions(text, entry.TargetFn);
            var description = $"function '{entry.TargetFn}'";
            if (spans.Count != 1)
                return new Region(null, description, $"function '{entry.TargetFn}' defined {spans.Count}x");

            var span = spans[0];
            return new Region(text[span.Start..(span.BodyClose + 1)], description, "", LineOf(text, span.Start));
        }

        var occurrences = CountOccurrences(text, entry.Anchor);
        if (occurrences != 1)
            return new Region(null, "the lines around the anchor", $"anchor matched {occurrences}x");

        var start = text.IndexOf(entry.Anchor, StringComparison.Ordinal);
        var end = start + entry.Anchor.Length;
        var enclosing = GmlScanner.EnclosingFunction(text, start);
        if (enclosing is not null)
        {
            var regionStart = Math.Min(enclosing.Start, start);
            var regionEnd = Math.Max(enclosing.BodyClose + 1, end);
            return new Region(text[regionStart..regionEnd],
                $"function '{enclosing.Name}' around the anchor", "", LineOf(text, regionStart));
        }

        var windowStart = WalkLines(text, start, -TopLevelContextLines);
        var windowEnd = WalkLines(text, end, TopLevelContextLines);
        return new Region(text[windowStart..windowEnd], "the lines around the anchor", "",
            LineOf(text, windowStart));
    }

    private static string? LoadFile(IPristineSource pristine, string path, Dictionary<string, string?> cache)
    {
        if (cache.TryGetValue(path, out var cached)) return cached;

        string? text = null;
        if (pristine.Read(path) is { } raw)
        {
            try
            {
                text = Utf8Strict.GetString(raw).Replace("\r\n", "\n");
            }
            catch (DecoderFallbackException)
            {
                // A non-UTF-8 engine file cannot be safely analysed.
            }
        }

        cache[path] = text;
        return text;
    }

    private static string TokenForm(string text) =>
        string.Join(" ", GmlScanner.Tokenize(text).Select(token => text[token.Start..token.End]));

    private static List<string> DiffLines(string oldText, string newText)
    {
        var oldLines = oldText.TrimEnd('\n').Split('\n');
        var newLines = newText.TrimEnd('\n').Split('\n');
        var lcs = new int[oldLines.Length + 1, newLines.Length + 1];
        for (var oldIndex = oldLines.Length - 1; oldIndex >= 0; oldIndex--)
        for (var newIndex = newLines.Length - 1; newIndex >= 0; newIndex--)
            lcs[oldIndex, newIndex] = oldLines[oldIndex] == newLines[newIndex]
                ? lcs[oldIndex + 1, newIndex + 1] + 1
                : Math.Max(lcs[oldIndex + 1, newIndex], lcs[oldIndex, newIndex + 1]);

        List<string> result = [];
        var oldAt = 0;
        var newAt = 0;
        while (oldAt < oldLines.Length && newAt < newLines.Length)
        {
            if (oldLines[oldAt] == newLines[newAt])
            {
                result.Add("  " + oldLines[oldAt]);
                oldAt++;
                newAt++;
            }
            else if (lcs[oldAt + 1, newAt] >= lcs[oldAt, newAt + 1])
            {
                result.Add("- " + oldLines[oldAt++]);
            }
            else
            {
                result.Add("+ " + newLines[newAt++]);
            }
        }

        while (oldAt < oldLines.Length) result.Add("- " + oldLines[oldAt++]);
        while (newAt < newLines.Length) result.Add("+ " + newLines[newAt++]);

        // Keep changed lines and a small neighborhood. A game update can
        // touch a very large function; dumping its entire body defeats the
        // point of a triage report.
        var keep = new bool[result.Count];
        for (var i = 0; i < result.Count; i++)
        {
            if (result[i].StartsWith("  ", StringComparison.Ordinal)) continue;
            for (var at = Math.Max(0, i - 3); at <= Math.Min(result.Count - 1, i + 3); at++) keep[at] = true;
        }

        List<string> compact = [];
        var hidden = 0;
        for (var i = 0; i < result.Count; i++)
        {
            if (!keep[i])
            {
                hidden++;
                continue;
            }

            if (hidden > 0)
            {
                compact.Add($"  ... ({hidden} unchanged lines)");
                hidden = 0;
            }

            compact.Add(result[i]);
        }

        if (hidden > 0) compact.Add($"  ... ({hidden} unchanged lines)");
        return compact;
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static int LineOf(string text, int position) => text.AsSpan(0, position).Count('\n') + 1;
    private static int LineCount(string text) => text.TrimEnd('\n').Split('\n').Length;

    private static int WalkLines(string text, int position, int count)
    {
        if (count < 0)
        {
            var at = GmlScanner.LineStart(text, position);
            for (var i = 0; i > count && at > 0; i--) at = GmlScanner.LineStart(text, at - 1);
            return at;
        }

        var atForward = position;
        for (var i = 0; i < count && atForward < text.Length; i++)
            atForward = GmlScanner.NextLineStart(text, atForward);
        return atForward;
    }

    public static string RenderText(SeamDiffResult result, string oldSource, string newSource)
    {
        List<string> lines =
        [
            $"seam-diff {oldSource} -> {newSource}",
            $"  catalog: {result.Entries.Count} regions compared",
        ];

        if (result.ModdedMarkers.Count > 0)
        {
            lines.Add("  RESULT: REFUSED - catalog markers found; a compared build is modded, not pristine:");
            lines.AddRange(result.ModdedMarkers.Select(marker => "  - " + marker));
            lines.Add("  Uninstall first, or pass two pristine archives.");
            return string.Join("\n", lines);
        }

        if (result.Ok)
        {
            lines.Add("  RESULT: OK - no seam region changed between these builds");
            return string.Join("\n", lines);
        }

        lines.Add($"  RESULT: REVIEW - {result.ChangedCount} changed, {result.MissingCount} missing");
        foreach (var entry in result.Entries.Where(entry => entry.Status != SeamRegionStatus.Unchanged))
        {
            var status = entry.Status switch
            {
                SeamRegionStatus.Changed => "CHANGED",
                SeamRegionStatus.OldMissing => "OLD MISSING - " + entry.Note,
                _ => "NEW MISSING - " + entry.Note,
            };
            lines.Add("");
            lines.Add($"  - {entry.Kind} '{entry.EntryId}': {entry.Region} in {entry.File}: {status}");
            lines.AddRange(entry.DiffLines.Select(line => "    " + line));
        }

        lines.Add("");
        lines.Add("  Changed regions may still stage. Review each hook contract, then run --seam-check.");
        return string.Join("\n", lines);
    }

    public static string ToJson(SeamDiffResult result, string oldSource, string newSource) => new JObject
    {
        ["ok"] = result.Ok,
        ["old"] = oldSource,
        ["new"] = newSource,
        ["regions"] = result.Entries.Count,
        ["changed"] = result.ChangedCount,
        ["missing"] = result.MissingCount,
        ["modded_markers"] = new JArray(result.ModdedMarkers),
        ["entries"] = new JArray(result.Entries.Select(entry => new JObject
        {
            ["id"] = entry.EntryId,
            ["kind"] = entry.Kind,
            ["file"] = entry.File,
            ["region"] = entry.Region,
            ["status"] = entry.Status.ToString().ToSnakeCase(),
            ["note"] = entry.Note,
            ["region_start_line"] = entry.RegionStartLine,
            ["region_lines"] = entry.RegionLines,
            ["old_text"] = entry.OldText,
            ["new_text"] = entry.NewText,
            ["diff"] = new JArray(entry.DiffLines),
        })),
    }.ToString(Formatting.Indented);

    private static string ToSnakeCase(this string value) => value switch
    {
        nameof(SeamRegionStatus.OldMissing) => "old_missing",
        nameof(SeamRegionStatus.NewMissing) => "new_missing",
        _ => value.ToLowerInvariant(),
    };
}
