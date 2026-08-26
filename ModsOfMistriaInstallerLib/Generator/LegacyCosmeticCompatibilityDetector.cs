using Garethp.ModsOfMistriaInstallerLib.Models.MOMI;
using Garethp.ModsOfMistriaInstallerLib.ModTypes;
using SixLabors.ImageSharp;

namespace Garethp.ModsOfMistriaInstallerLib.Generator;

/// <summary>
/// Inspects legacy <c>momi/outfit</c> definitions without modifying the mod.
/// AIM still supports this format through <see cref="OutfitGenerator"/>; this
/// detector only reports facts that would make generated assets incomplete.
/// </summary>
public static class LegacyCosmeticCompatibilityDetector
{
    public sealed record Result(bool UsesLegacyFormat, IReadOnlyList<string> Issues);

    public static Result Analyze(IMod mod)
    {
        var files = mod.GetFilesInFolder("momi/outfit", ".toml");
        if (files.Count == 0) return new Result(false, []);

        var issues = new List<string>();
        foreach (var file in files)
        {
            var relativePath = OutfitGenerator.Relativize(mod, file);
            var content = mod.ReadFile(relativePath);
            if (string.IsNullOrWhiteSpace(content))
            {
                issues.Add($"{relativePath}: the outfit definition could not be read.");
                continue;
            }

            var definitions = OutfitDefinition.ParseAll(content);
            if (definitions.Count == 0)
            {
                issues.Add($"{relativePath}: no outfit definitions could be parsed.");
                continue;
            }

            foreach (var definition in definitions.Values)
                ValidateDefinition(mod, relativePath, definition, issues);
        }

        return new Result(true, issues);
    }

    private static void ValidateDefinition(IMod mod, string definitionPath, OutfitFile definition, List<string> issues)
    {
        var label = $"{definitionPath} [{definition.Id}]";
        if (string.IsNullOrWhiteSpace(definition.Id) || string.IsNullOrWhiteSpace(definition.UiSlot))
        {
            issues.Add($"{label}: missing id or ui_slot.");
            return;
        }

        if (!OutfitGenerator.TryGetFrameSize(definition.UiSlot, out var defaultWidth, out var defaultHeight))
        {
            issues.Add($"{label}: unsupported ui_slot '{definition.UiSlot}'.");
            return;
        }

        var frameWidth = definition.FrameWidth ?? defaultWidth;
        var frameHeight = definition.FrameHeight ?? defaultHeight;
        if (frameWidth <= 0 || frameHeight <= 0)
        {
            issues.Add($"{label}: frame_size must contain positive width and height values.");
            return;
        }

        ValidateUiSprites(mod, definition, label, issues);

        var playerFolder = OutfitGenerator.GetPlayerFolder(definition.UiSlot)!;
        var parts = OutfitGenerator.GetParts(definition.UiSlot);
        if (parts is { Length: > 0 })
        {
            var partPaths = parts
                .Select(part => $"animations/{playerFolder}/spr_player_{definition.Id}_{part}.png")
                .Where(mod.FileExists)
                .ToList();
            if (partPaths.Count == 0)
            {
                issues.Add($"{label}: no player animation part was found for slot '{definition.UiSlot}'.");
                return;
            }

            foreach (var path in partPaths)
                ValidateStrip(mod, path, frameWidth, frameHeight, label, issues);
            return;
        }

        var sprite = OutfitGenerator.ResolveOutfitSprite(definition);
        var spritePath = $"animations/{playerFolder}/{sprite}.png";
        if (!mod.FileExists(spritePath))
        {
            issues.Add($"{label}: missing player animation '{spritePath}'.");
            return;
        }

        ValidateStrip(mod, spritePath, frameWidth, frameHeight, label, issues);
    }

    private static void ValidateUiSprites(IMod mod, OutfitFile definition, string label, List<string> issues)
    {
        var basePath = "animations/Item Icons/Wearable/";
        var expected = OutfitGenerator.IsComplexSlot(definition.UiSlot)
            ? new[] { "_asset", "_body", "_merged", "_merged_outline" }
                .Select(suffix => $"{basePath}{definition.ResolvedIconSprite}{suffix}.png")
            : new[]
            {
                $"{basePath}{definition.ResolvedIconSprite}.png",
                $"{basePath}{definition.ResolvedOutlineSprite}.png"
            };

        foreach (var path in expected)
        {
            if (!mod.FileExists(path))
                issues.Add($"{label}: missing UI sprite '{path}'.");
        }
    }

    private static void ValidateStrip(IMod mod, string path, int frameWidth, int frameHeight, string label, List<string> issues)
    {
        try
        {
            using var stream = mod.ReadFileAsStream(path);
            var info = Image.Identify(stream);
            if (info.Width % frameWidth != 0 || info.Height != frameHeight)
                issues.Add($"{label}: '{path}' is {info.Width}x{info.Height}; expected a horizontal strip with {frameWidth}px frames and {frameHeight}px height.");
        }
        catch (Exception)
        {
            issues.Add($"{label}: '{path}' could not be read as a PNG sprite strip.");
        }
    }
}
