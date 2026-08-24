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

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
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
