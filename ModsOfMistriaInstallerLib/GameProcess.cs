using System.Diagnostics;

namespace Garethp.ModsOfMistriaInstallerLib;

/// <summary>
/// Whether Fields of Mistria is running right now.
///
/// This gates editing mod settings. The game reads them at launch and writes them back on exit, so
/// an edit made while it is running is overwritten the moment the player quits - and it looks for
/// all the world like AIM silently lost the change.
/// </summary>
public static class GameProcess
{
    private static readonly string[] Names = ["FieldsOfMistria", "Fields of Mistria"];

    public static bool IsRunning()
    {
        foreach (var name in Names)
        {
            try
            {
                // GetProcessesByName hands back live handles; disposing them keeps a check that
                // runs on every window open from leaking one per running process.
                var processes = Process.GetProcessesByName(name);
                try
                {
                    if (processes.Length > 0) return true;
                }
                finally
                {
                    foreach (var process in processes) process.Dispose();
                }
            }
            catch (Exception exception)
            {
                // Enumerating processes can be refused outright on a locked-down machine. Try the
                // other spelling rather than giving up: failing one name says nothing about the
                // next. If every name fails the method falls through to "not running", which is the
                // safe answer - it lets the user edit, and the worst case is a warning they miss.
                Logger.Log($"Could not check whether {name} is running: {exception.Message}");
            }
        }

        return false;
    }
}
