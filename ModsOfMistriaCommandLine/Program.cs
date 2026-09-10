// See https://aka.ms/new-console-template for more information

using System.Reflection;
using Garethp.ModsOfMistriaInstallerLib;
using Garethp.ModsOfMistriaInstallerLib.GmlMods;
using Garethp.ModsOfMistriaInstallerLib.Lang;
using Garethp.ModsOfMistriaInstallerLib.ModTypes;
using Garethp.ModsOfMistriaInstallerLib.Operations;
using Garethp.ModsOfMistriaInstallerLib.Seam;
using Garethp.ModsOfMistriaInstallerLib.Tools;
using Garethp.ModsOfMistriaInstallerLib.Store;
using Garethp.ModsOfMistriaInstallerLib.Generator;
using Garethp.ModsOfMistriaCommandLine;

var currentExe = Assembly.GetEntryAssembly();
var currentVersionString =
    currentExe!.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version ?? "0.1.0";

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine(CliUsage());
    Environment.Exit(0);
}

if (args.Contains("--version"))
{
    Console.WriteLine(currentVersionString);
    Environment.Exit(0);
}

var outputFormat = ParseOutputFormat(args, out var outputFormatError);
if (outputFormatError is not null)
{
    Console.Error.WriteLine($"Error: {outputFormatError}");
    Environment.Exit(2);
}

if (!ValidateArguments(args, out var argumentError))
{
    Console.Error.WriteLine($"Error: {argumentError}");
    Console.Error.WriteLine();
    Console.Error.WriteLine(CliUsage());
    Environment.Exit(2);
}

// The CLI parses and prints; the behaviour behind each flag lives in the Lib.
// Exit codes: 0 ok, 1 error, 2 usage.
var gateMode = CompileGateMode.Auto;
switch (FlagValue(args, "--compile-check"))
{
    case null or "on":  // "on" is the default made explicit: run when a backend resolves
        break;
    case "off":
        gateMode = CompileGateMode.Off;
        break;
    case "require":
        gateMode = CompileGateMode.Mandatory;
        break;
    default:
        Console.WriteLine(Resources.CLICompileCheckUsage);
        Environment.Exit(2);
        break;
}

// The seam check runs before the console subscribes to Logger, so its report
// (JSON included) is the only thing on stdout.
if (args.Contains("--seam-check") || args.Contains("--seam-check-json"))
{
    Environment.Exit(RunSeamCheck(args));
}

// Lint likewise: the stage's own log lines stay internal and the report is
// the only thing on stdout.
if (args.Contains("--lint"))
{
    Environment.Exit(RunLint(args, gateMode));
}

if (args.Contains("--list-mods"))
{
    Environment.Exit(RunListMods(outputFormat));
}

if (args.Contains("--status"))
{
    Environment.Exit(RunStatus(outputFormat));
}

if (args.Contains("--doctor"))
{
    Environment.Exit(RunDoctor(outputFormat));
}

if (args.Contains("--dry-run"))
{
    if (!args.Contains("--install"))
    {
        Console.Error.WriteLine("Error: --dry-run must be combined with --install.");
        Environment.Exit(2);
    }

    Environment.Exit(RunDryRun(outputFormat, gateMode, args.Contains("--strict-lints")));
}

if (!args.Contains("--install") && !args.Contains("--uninstall"))
{
    Console.Error.WriteLine("Error: choose an action: --install or --uninstall.");
    Console.Error.WriteLine();
    Console.Error.WriteLine(CliUsage());
    Environment.Exit(2);
}

Logger.LogAdded += (_, e) => Console.WriteLine(e.Message);

Logger.Log(Resources.CLIRunningBuild, currentVersionString);

if (Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess)
{
    Logger.Log(Resources.CLIWarning32Bit);
}

var exitCode = 0;
if (args.Contains("--uninstall"))
{
    try
    {
        Standalone.UnInstall();
        Logger.Log(Resources.CLIUninstallComplete);
    }
    catch (Exception exception)
    {
        Logger.Log(exception.Message);
        exitCode = 1;
    }
}
else
{
    var gmlOptions = new GmlLayerOptions
    {
        StrictLints = args.Contains("--strict-lints"),
        FailOnSkip = args.Contains("--fail-on-skip"),
    };

    try
    {
        Standalone.Run(gmlOptions, gateMode);
        Logger.Log(Resources.CLICompleted);
    }
    catch (Exception exception)
    {
        Logger.Log(exception.Message);
        exitCode = 1;
    }
}

if (Environment.GetEnvironmentVariable("EXIT_ON_COMPLETE") != "true" &&
    !Console.IsInputRedirected && !Console.IsOutputRedirected)
{
    Console.ReadKey();
}

Environment.Exit(exitCode);

// The arg after the flag, "" when the flag is last, null when absent
static string? FlagValue(string[] args, string flag)
{
    var index = Array.IndexOf(args, flag);
    if (index < 0) return null;
    return index + 1 < args.Length ? args[index + 1] : "";
}

static bool ValidateArguments(string[] args, out string error)
{
    var commands = new List<string>();
    for (var i = 0; i < args.Length; i++)
    {
        var argument = args[i];
        switch (argument)
        {
            case "--install":
            case "--uninstall":
                commands.Add(argument);
                break;
            case "--lint":
                commands.Add(argument);
                if (!TakeRequiredValue(args, ref i, argument, out error)) return false;
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) i++;
                break;
            case "--seam-check":
            case "--seam-check-json":
                commands.Add(argument);
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) i++;
                break;
            case "--list-mods":
            case "--status":
            case "--doctor":
                commands.Add(argument);
                break;
            case "--json":
            case "--toml":
            case "--format":
                if (argument == "--format" && !TakeRequiredValue(args, ref i, argument, out error)) return false;
                break;
            case "--compile-check":
                if (!TakeRequiredValue(args, ref i, argument, out error)) return false;
                if (args[i] is not ("on" or "off" or "require"))
                {
                    error = $"{argument} expects on, off, or require.";
                    return false;
                }
                break;
            case "--strict-lints":
            case "--fail-on-skip":
            case "--dry-run":
                break;
            default:
                error = $"unknown argument '{argument}'.";
                return false;
        }
    }

    if (commands.Count == 0)
    {
        error = "no action was specified.";
        return false;
    }

    if (commands.Count > 1)
    {
        error = $"actions cannot be combined: {string.Join(", ", commands)}.";
        return false;
    }

    error = string.Empty;
    return true;
}

static bool TakeRequiredValue(string[] args, ref int index, string flag, out string error)
{
    if (index + 1 >= args.Length || args[index + 1].StartsWith("--"))
    {
        error = $"{flag} requires a value.";
        return false;
    }

    index++;
    error = string.Empty;
    return true;
}

static string CliUsage() => """
Usage:
  AIM-cli --install [--strict-lints] [--fail-on-skip] [--compile-check on|off|require]
  AIM-cli --dry-run --install [--strict-lints] [--compile-check on|off|require] [--json|--toml]
  AIM-cli --uninstall
  AIM-cli --lint <mod-folder> [pristine-assets.zip] [--strict-lints] [--compile-check on|off|require]
  AIM-cli --seam-check [pristine-assets.zip]
  AIM-cli --seam-check-json [pristine-assets.zip]
  AIM-cli --list-mods [--json|--toml]
  AIM-cli --status [--json|--toml]
  AIM-cli --doctor [--json|--toml]
  AIM-cli --version
  AIM-cli --help

Exit codes:
  0  completed successfully
  1  operation failed or the lint would skip the mod
  2  invalid usage or missing input
""";

static CliOutputFormat ParseOutputFormat(string[] args, out string? error)
{
    error = null;
    if (args.Contains("--json") && args.Contains("--toml"))
    {
        error = "--json and --toml cannot be combined.";
        return CliOutputFormat.Human;
    }

    var explicitFormat = FlagValue(args, "--format");
    if (explicitFormat is not null && explicitFormat is not ("human" or "json" or "toml"))
    {
        error = "--format expects human, json, or toml.";
        return CliOutputFormat.Human;
    }

    if (args.Contains("--json") || explicitFormat == "json") return CliOutputFormat.Json;
    if (args.Contains("--toml") || explicitFormat == "toml") return CliOutputFormat.Toml;
    return CliOutputFormat.Human;
}

static int RunListMods(CliOutputFormat format)
{
    var game = MistriaLocator.GetMistriaLocation();
    var modsPath = MistriaLocator.GetModsLocation(game);
    if (game is null || modsPath is null)
    {
        Console.Error.WriteLine("Could not locate the game or mods folder.");
        return 2;
    }

    var mods = MistriaLocator.GetMods(game, modsPath);
    ModInstaller.ValidateMods(mods);
    var reports = mods.Select(CliReports.Mod).ToArray();
    var value = new { command = "list-mods", ok = true, game, mods_path = modsPath, mods = reports };
    var human = string.Join(Environment.NewLine,
        new[] { $"Game: {game}", $"Mods: {modsPath}", $"Found: {reports.Length}" }
            .Concat(reports.Select(mod => $"{mod.Validation,-7} {mod.Name} v{mod.Version} by {mod.Author} [{mod.Source}]")));
    CliReports.Write(value, human, format);
    return 0;
}

static int RunStatus(CliOutputFormat format)
{
    var game = MistriaLocator.GetMistriaLocation();
    var modsPath = MistriaLocator.GetModsLocation(game);
    var store = game is null ? null : new AssetsStore(game);
    var state = store?.GetRecordedInstallState();
    var recovery = store?.AssessForeignArchiveRecovery();
    var value = new
    {
        command = "status",
        ok = game is not null && modsPath is not null,
        game,
        mods_path = modsPath,
        assets = new
        {
            live = store is not null && File.Exists(store.LivePath),
            backup = store is not null && File.Exists(store.BackupPath),
            state = state is not null,
            recovery = recovery?.Status.ToString(),
            recovery_reason = recovery?.Reason
        },
        recorded_mods = state?.Mods ?? []
    };
    var human = $"Game: {game ?? "not found"}\nMods: {modsPath ?? "not found"}\n" +
                $"assets.zip: {(value.assets.live ? "present" : "missing")}\n" +
                $"assets.bak.zip: {(value.assets.backup ? "present" : "missing")}\n" +
                $"AIM state: {(value.assets.state ? "present" : "missing")}\n" +
                $"Recovery: {value.assets.recovery ?? "unavailable"}";
    CliReports.Write(value, human, format);
    return value.ok ? 0 : 2;
}

static int RunDoctor(CliOutputFormat format)
{
    var game = MistriaLocator.GetMistriaLocation();
    var modsPath = MistriaLocator.GetModsLocation(game);
    var checks = new List<object>();
    checks.Add(new { name = "game", ok = game is not null, message = game ?? "Fields of Mistria was not found." });
    checks.Add(new { name = "mods", ok = modsPath is not null, message = modsPath ?? "Mods folder was not found." });

    ForeignArchiveRecoveryAssessment? recovery = null;
    if (game is not null)
    {
        var store = new AssetsStore(game);
        recovery = store.AssessForeignArchiveRecovery();
        checks.Add(new
        {
            name = "assets",
            ok = recovery.Status != ForeignArchiveRecoveryStatus.Blocked,
            message = recovery.Reason,
            recovery = recovery.Status.ToString()
        });
    }
    else checks.Add(new { name = "assets", ok = false, message = "Cannot inspect assets without the game location." });

    var ok = checks.All(check => (bool)check.GetType().GetProperty("ok")!.GetValue(check)!);
    var value = new { command = "doctor", ok, game, mods_path = modsPath, checks };
    var human = string.Join(Environment.NewLine, new[] { $"Doctor: {(ok ? "OK" : "PROBLEMS FOUND")}" }
        .Concat(checks.Select(check =>
        {
            var type = check.GetType();
            return $"{(type.GetProperty("ok")!.GetValue(check) is true ? "OK" : "FAIL")} " +
                   $"{type.GetProperty("name")!.GetValue(check)}: {type.GetProperty("message")!.GetValue(check)}";
        })));
    CliReports.Write(value, human, format);
    return ok ? 0 : 1;
}

static int RunDryRun(CliOutputFormat format, CompileGateMode gateMode, bool strictLints)
{
    var game = MistriaLocator.GetMistriaLocation();
    var modsPath = MistriaLocator.GetModsLocation(game);
    if (game is null || modsPath is null)
    {
        Console.Error.WriteLine("Could not locate the game or mods folder.");
        return 2;
    }

    var store = new AssetsStore(game);
    if (!File.Exists(store.BackupPath))
    {
        Console.Error.WriteLine($"Cannot run the dry-run: pristine backup not found at '{store.BackupPath}'.");
        return 2;
    }

    var mods = MistriaLocator.GetMods(game, modsPath);
    ModInstaller.ValidateMods(mods);
    var options = new GmlLayerOptions { StrictLints = strictLints, FailOnSkip = false };
    var results = new List<object>();
    var failed = false;

    using var pristine = new ZipPristineSource(store.BackupPath);
    var gate = GmlCompileGate.Resolve(gateMode);
    foreach (var mod in mods)
    {
        try
        {
            var result = ModLinter.Lint(mod, pristine, gate, options);
            failed |= !result.Ok;
            results.Add(new
            {
                id = result.ModId,
                version = result.Version,
                source = mod.GetSourcePath(),
                ok = result.Ok,
                gml_files = result.GmlFileCount,
                gate_ran = result.GateRan,
                errors = result.ManifestErrors,
                warnings = result.ManifestWarnings,
                findings = result.Findings,
                exclusions = result.ExclusionReasons
            });
        }
        catch (SeamStagingException exception)
        {
            failed = true;
            results.Add(new { id = mod.GetId(), version = mod.GetVersion(), source = mod.GetSourcePath(), ok = false, error = exception.Message });
        }
    }

    var value = new
    {
        command = "dry-run",
        ok = !failed,
        writes = false,
        game,
        mods_path = modsPath,
        results
    };
    var human = $"Dry-run: {(failed ? "FAIL" : "OK")} (no files were written)\n" +
                string.Join(Environment.NewLine, results.Select(result =>
                {
                    var type = result.GetType();
                    var ok = type.GetProperty("ok")?.GetValue(result) as bool?;
                    var id = type.GetProperty("id")?.GetValue(result);
                    return $"{(ok is true ? "OK" : "FAIL")} {id}";
                }));
    CliReports.Write(value, human, format);
    return failed ? 1 : 0;
}

// --seam-check [zip] / --seam-check-json [zip]: the located install's backup
// when no zip is given. Exit 0 when every anchor holds, 1 when any broke, 2
// when there is nothing to check against.
static int RunSeamCheck(string[] args)
{
    var zipPath = FlagValue(args, "--seam-check");
    if (IsMissing(zipPath)) zipPath = FlagValue(args, "--seam-check-json");

    if (IsMissing(zipPath))
    {
        var mistriaLocation = MistriaLocator.GetMistriaLocation();
        if (mistriaLocation is null)
        {
            Console.WriteLine(Resources.CoreMistriaNotFound);
            return 2;
        }

        try
        {
            zipPath = SeamVerifier.LocateBackup(mistriaLocation);
        }
        catch (FileNotFoundException exception)
        {
            Console.WriteLine(exception.Message);
            return 2;
        }
    }

    VerifyResult result;
    try
    {
        using var pristine = new ZipPristineSource(zipPath!);
        result = SeamVerifier.Verify(pristine);
    }
    catch (FileNotFoundException exception)
    {
        Console.WriteLine(exception.Message);
        return 2;
    }

    Console.WriteLine(args.Contains("--seam-check-json")
        ? SeamVerifier.ToJson(result, zipPath!)
        : SeamVerifier.RenderText(result, zipPath!));
    return result.ExitCode;
}

// --lint <mod folder> [zip]: would the apply install this mod? Runs the
// manifest validation and the full read-only GML staging - skip pass, lints
// and compile gate - against the pristine zip (the located install's backup
// when none is given) and prints the report. --strict-lints and
// --compile-check compose exactly as they do on an install. Exit 0 when the
// mod would install, 1 when it would be skipped, 2 when the lint cannot run.
static int RunLint(string[] args, CompileGateMode gateMode)
{
    var modPath = FlagValue(args, "--lint");
    if (IsMissing(modPath))
    {
        Console.WriteLine(Resources.CLILintUsage);
        return 2;
    }

    // the optional pristine zip rides as a second positional, --seam-check style
    var lintIndex = Array.IndexOf(args, "--lint");
    var zipPath = lintIndex + 2 < args.Length && !args[lintIndex + 2].StartsWith("--")
        ? args[lintIndex + 2]
        : null;

    if (zipPath is null)
    {
        var mistriaLocation = MistriaLocator.GetMistriaLocation();
        if (mistriaLocation is null)
        {
            Console.WriteLine(Resources.CoreMistriaNotFound);
            return 2;
        }

        try
        {
            zipPath = SeamVerifier.LocateBackup(mistriaLocation);
        }
        catch (FileNotFoundException exception)
        {
            Console.WriteLine(exception.Message);
            return 2;
        }
    }

    var location = FolderMod.GetModLocation(modPath!);
    if (location is null)
    {
        Console.WriteLine(Resources.CoreCouldNotFindModManifest);
        return 2;
    }

    try
    {
        var mod = FolderMod.FromManifest(location);
        var options = new GmlLayerOptions { StrictLints = args.Contains("--strict-lints") };
        using var pristine = new ZipPristineSource(zipPath);
        var result = ModLinter.Lint(mod, pristine, GmlCompileGate.Resolve(gateMode), options);
        Console.WriteLine(ModLinter.RenderText(result, location));
        return result.ExitCode;
    }
    catch (Exception exception)
    {
        // an unreadable zip, stale anchors or a malformed manifest: the lint
        // could not run, which says nothing about the mod itself
        Console.WriteLine(exception.Message);
        return 2;
    }
}

static bool IsMissing(string? zipPath) =>
    zipPath is null || zipPath.Length == 0 || zipPath.StartsWith("--");
