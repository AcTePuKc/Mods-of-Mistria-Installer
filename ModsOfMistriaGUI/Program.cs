using Avalonia;
using Garethp.ModsOfMistriaInstallerLib.Nexus;
using Projektanker.Icons.Avalonia;
using Projektanker.Icons.Avalonia.FontAwesome;

namespace Garethp.ModsOfMistriaGUI;

public static class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        if (Garethp.ModsOfMistriaInstallerLib.Worker.ArchiveWorkerRunner.IsWorkerInvocation(args))
        {
            Garethp.ModsOfMistriaInstallerLib.Worker.ArchiveWorkerRunner.RunAsync(args).GetAwaiter().GetResult();
            return;
        }

        // The browser starts a fresh process for every clicked "Mod Manager Download" link. If a
        // window is already open it should take the download, so the link is handed over and this
        // process exits without ever building a UI.
        var nxmLink = args.FirstOrDefault(NxmLink.IsNxmUri);
        if (nxmLink is not null && NxmLinkListener.TrySend(nxmLink)) return;

        App.StartupNxmLink = nxmLink;

        // AIM's log lives in memory, which is exactly the wrong place when the process dies: the
        // one crash the user wants to report is the one that leaves nothing behind. These two hooks
        // write it out on the way down.
        InstallCrashLog();

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    private static void InstallCrashLog()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteCrashLog(e.ExceptionObject as Exception, "unhandled");

        // A faulted Task nobody awaited does not stop the process, but it is usually the first sign
        // of the bug that later does, so it is worth the same record.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteCrashLog(e.Exception, "unobserved-task");
            e.SetObserved();
        };
    }

    private static void WriteCrashLog(Exception? exception, string kind)
    {
        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIM", "crashes");
            Directory.CreateDirectory(folder);

            var path = Path.Combine(
                folder, $"aim-crash-{DateTime.Now:yyyyMMdd-HHmmss}-{kind}.log");

            var lines = new List<string>
            {
                $"AIM crash ({kind}) at {DateTimeOffset.Now:O}",
                exception?.ToString() ?? "No exception object was supplied.",
                "",
                "── Session log ──"
            };
            lines.AddRange(Garethp.ModsOfMistriaInstallerLib.Logger.GetLogs());

            File.WriteAllText(path, string.Join(Environment.NewLine, lines));
        }
        catch
        {
            // Writing the crash log must never be what crashes.
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    private static AppBuilder BuildAvaloniaApp()
    {
        IconProvider.Current.Register<FontAwesomeIconProvider>();
        
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
