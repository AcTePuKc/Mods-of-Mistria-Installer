using System.Diagnostics;
using System.Text;

namespace Garethp.ModsOfMistriaInstallerLib.Crash;

/// <summary>How a supervised run of the game ended.</summary>
/// <param name="Crashed">
/// True when the game left a crash file behind that was not there before, or exited non-zero.
/// A clean exit with no new crash file is the answer "it worked".
/// </param>
/// <param name="Crash">The crash it captured, if there was one.</param>
public sealed record GameRunOutcome(
    bool Started,
    bool Crashed,
    int? ExitCode,
    TimeSpan Duration,
    GameCrashLog? Crash,
    string? LogPath,
    string Message)
{
    /// <summary>
    /// The run ended badly but not in a way that says anything about the mod being tested: the
    /// process was killed from outside, or it left a crash log AIM could not read.
    ///
    /// Kept apart from <see cref="Crashed"/> because the caller's question is not "did something go
    /// wrong" but "did the crash under investigation happen again", and a run that cannot answer
    /// that must be retried rather than counted. Treating one as a clearance would rule out a mod
    /// on the strength of a graphics driver falling over.
    /// </summary>
    public bool Inconclusive { get; init; }
}

/// <summary>
/// Runs the game with AIM watching, so that a crash is recorded rather than merely happening.
///
/// Three things are true of the game's own crash file that make it a poor thing to rely on alone:
/// it is overwritten by the next crash, it carries no record of which mods were installed, and it
/// is not written at all for the failures that kill the process outright. A supervised run fixes
/// all three. The process is started directly rather than through Steam - Steam hands back a
/// launcher that exits immediately, so there would be nothing to wait on - its output is kept, and
/// the crash file is captured the moment the run ends and stamped with the load order that
/// produced it.
///
/// This is used to verify a fix: disable the suspect, rebuild, run, and see whether the same crash
/// comes back. It is not used for ordinary Play, which stays exactly as it was, because a player
/// launching their game does not want AIM holding a handle on it.
/// </summary>
public static class GameRunRecorder
{
    private static string LogFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIM", "game-runs");

    /// <summary>
    /// Starts the game, waits for it to end, and reports what happened.
    /// </summary>
    /// <param name="mods">Enabled mods in load order, as "id version" strings, for the capture.</param>
    /// <param name="installedAt">When the archive being tested was published.</param>
    /// <param name="before">
    /// The crash that was already on disk. A run is only judged to have crashed if what it leaves
    /// behind is different from this - otherwise a stale file from last week would condemn a fix
    /// that actually worked.
    /// </param>
    /// <param name="timeout">
    /// How long to wait before giving up on the run. Not a failure: a user who is still playing
    /// after the timeout has demonstrated the crash did not recur, which is the answer wanted.
    /// </param>
    public static async Task<GameRunOutcome> RunAsync(
        string mistriaLocation,
        IReadOnlyList<string> mods,
        DateTimeOffset? installedAt,
        GameCrashLog? before,
        CrashArchive archive,
        TimeSpan timeout,
        CancellationToken cancellation = default)
    {
        var executable = GameExecutableLocator.Find(mistriaLocation);

        if (executable is null)
            return new GameRunOutcome(false, false, null, TimeSpan.Zero, null, null,
                "AIM could not find the game's executable, so it cannot run it and watch.");

        if (GameProcess.IsRunning())
            return new GameRunOutcome(false, false, null, TimeSpan.Zero, null, null,
                "Fields of Mistria is already running. Close it first, so AIM watches the run it started.");

        var started = DateTimeOffset.Now;
        var logPath = Path.Combine(LogFolder, $"run-{started.UtcDateTime:yyyyMMdd-HHmmss}.log");
        var output = new StringBuilder();

        try
        {
            Directory.CreateDirectory(LogFolder);

            output.AppendLine($"AIM supervised run at {started:O}");
            output.AppendLine($"game: {executable}");
            output.AppendLine($"install published: {installedAt?.ToString("O") ?? "unknown"}");
            output.AppendLine($"{mods.Count} mods in load order:");
            foreach (var mod in mods) output.AppendLine("  " + mod);
            output.AppendLine("── game output ──────────────────────────────");
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not open a run log: {exception.Message}");
            logPath = null;
        }

        using var process = new Process();

        process.StartInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = mistriaLocation,

            // Required for redirection, and the reason this cannot go through the Steam URI. The
            // game still finds Steam through steam_api64.dll in its own folder, so achievements and
            // cloud saves are unaffected by being started this way.
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        // A GameMaker build says very little on stdout, but "very little" is not "nothing": a mod
        // that logs its own failure before the runtime notices puts it here and nowhere else.
        process.OutputDataReceived += (_, args) => { if (args.Data is not null) lock (output) output.AppendLine(args.Data); };
        process.ErrorDataReceived += (_, args) => { if (args.Data is not null) lock (output) output.AppendLine("stderr: " + args.Data); };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not start the game for a supervised run: {exception}");
            return new GameRunOutcome(false, false, null, TimeSpan.Zero, null, logPath,
                $"AIM could not start the game: {exception.Message}");
        }

        int? exitCode = null;
        var timedOut = false;

        try
        {
            using var window = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            window.CancelAfter(timeout);

            await process.WaitForExitAsync(window.Token);
            exitCode = process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            // Deliberately does not kill the game. The user is either still playing - which is the
            // result AIM was hoping for - or has been asked to stop and has not yet; killing their
            // session to tidy up a check would be indefensible either way.
            timedOut = true;
        }
        catch (Exception exception)
        {
            Logger.Log($"Waiting for the game to exit failed: {exception.Message}");
        }

        var duration = DateTimeOffset.Now - started;

        Write(logPath, output, exitCode, duration, timedOut);

        // Whether this run crashed is decided by the clock, not by the contents of the crash file.
        // The game writes that file as it dies, so a file last written before AIM started the
        // process belongs to some earlier run - and its contents cannot tell you that, because the
        // crash a check is hunting looks exactly like the crash it left behind last time. Comparing
        // reports instead is what made a successful run read as a repeat crash, and so reported the
        // guilty mod as ruled out.
        var crashFile = CrashArchive.CrashFileWrittenAt();
        var crashedThisRun = crashFile is not null && crashFile > started;

        // Captured after the wait, and only when the file is this run's, so a check cannot file a
        // crash that predates it under the load order it was testing.
        archive.Capture(mods, installedAt, note: "supervised run", onlyIfCrashedSince: started);

        // Which crash it was, for the window to compare against the one it is investigating. Taken
        // from the archive rather than from the file so it carries the load order just recorded.
        var after = crashedThisRun ? archive.Latest() : null;
        var isNew = crashedThisRun;

        // A crash log AIM cannot read is not evidence in either direction, and must not be allowed
        // to pass as "the same crash came back" - which is a clearance for the mod on trial.
        if (isNew && after is null)
            return new GameRunOutcome(true, true, exitCode, duration, null, logPath,
                "The game crashed, but AIM could not read the crash log it left, so this run proved " +
                "nothing about the mod. Worth running the check again.")
            {
                Inconclusive = true
            };

        if (timedOut)
            return new GameRunOutcome(true, isNew, null, duration, isNew ? after : null, logPath,
                isNew
                    ? "The game crashed again during the check."
                    : "The game is still running after the check window, and has not crashed. " +
                      "That is the result you wanted - carry on playing.");

        if (isNew)
        {
            // Whether it is the same fault or a new one is the caller's decision to act on, but it
            // costs nothing to say which happened, and "a different crash this time" is a very
            // different thing to read than "it crashed again".
            var repeat = before is not null &&
                         after is not null &&
                         string.Equals(after.StableKey, before.StableKey, StringComparison.Ordinal);

            return new GameRunOutcome(true, true, exitCode, duration, after, logPath,
                repeat
                    ? $"The same crash came back after {Describe(duration)}."
                    : $"The game crashed again after {Describe(duration)}, with a different fault.");
        }

        if (exitCode is not null and not 0)
            return new GameRunOutcome(true, true, exitCode, duration, null, logPath,
                $"The game exited with code {exitCode} after {Describe(duration)} and left no crash log. " +
                "That usually means it was closed by something outside the game - a driver fault, or " +
                "the process being killed - rather than by a mod, so this run proved nothing either way.")
            {
                // Not a clearance. The crash being hunted did not happen, but neither did a clean
                // run: something else ended the game, and the mod on trial is no more or less
                // likely to be the cause than it was before.
                Inconclusive = true
            };

        return new GameRunOutcome(true, false, exitCode, duration, null, logPath,
            $"The game ran for {Describe(duration)} and closed normally, with no new crash.");
    }

    private static void Write(string? path, StringBuilder output, int? exitCode, TimeSpan duration, bool timedOut)
    {
        if (path is null) return;

        try
        {
            lock (output)
            {
                output.AppendLine("─────────────────────────────────────────────");
                output.AppendLine(timedOut
                    ? $"still running after {Describe(duration)}; AIM stopped waiting"
                    : $"exited with {exitCode?.ToString() ?? "an unknown code"} after {Describe(duration)}");

                File.WriteAllText(path, output.ToString());
            }
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not write the run log: {exception.Message}");
        }
    }

    private static string Describe(TimeSpan duration) =>
        duration.TotalMinutes >= 1
            ? $"{(int)duration.TotalMinutes}m {duration.Seconds}s"
            : $"{duration.TotalSeconds:0.#}s";
}
