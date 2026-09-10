using System.Text.Json;
using Garethp.ModsOfMistriaInstallerLib.ModTypes;
using Tomlyn;
using Tomlyn.Model;

namespace Garethp.ModsOfMistriaCommandLine;

internal enum CliOutputFormat
{
    Human,
    Json,
    Toml
}

internal static class CliReports
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static void Write(object value, string human, CliOutputFormat format)
    {
        switch (format)
        {
            case CliOutputFormat.Json:
                Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
                break;
            case CliOutputFormat.Toml:
                var json = JsonSerializer.SerializeToElement(value, JsonOptions);
                Console.WriteLine(TomlSerializer.Serialize(ToTomlTable(json)));
                break;
            default:
                Console.WriteLine(human);
                break;
        }
    }

    public static CliModReport Mod(IMod mod) => new(
        mod.GetId(), mod.GetName(), mod.GetAuthor(), mod.GetVersion(), mod.GetSourcePath(),
        mod.IsInstalled(), mod.GetValidation().Status.ToString(),
        mod.GetValidation().Errors.Select(error => error.Message).ToArray(),
        mod.GetValidation().Warnings.Select(warning => warning.Message).ToArray(),
        mod.GetRequiredHooks().ToArray());

    private static TomlTable ToTomlTable(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("A report root must be an object.");

        var table = new TomlTable();
        foreach (var property in element.EnumerateObject())
            table[property.Name] = ToTomlValue(property.Value) ?? "";
        return table;
    }

    private static object? ToTomlValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => ToTomlTable(element),
        JsonValueKind.Array => ToTomlArray(element),
        JsonValueKind.String => element.GetString() ?? "",
        JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number => element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => element.ToString()
    };

    private static TomlArray ToTomlArray(JsonElement element)
    {
        var array = new TomlArray();
        foreach (var item in element.EnumerateArray())
            array.Add(ToTomlValue(item) ?? "");
        return array;
    }
}

internal sealed record CliModReport(
    string Id,
    string Name,
    string Author,
    string Version,
    string Source,
    bool Installed,
    string Validation,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> RequiredHooks);
