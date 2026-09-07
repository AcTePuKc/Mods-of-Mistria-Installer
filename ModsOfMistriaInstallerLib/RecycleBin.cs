using System.Runtime.InteropServices;

namespace Garethp.ModsOfMistriaInstallerLib;

/// <summary>
/// Moves a file or folder to the Windows Recycle Bin.
///
/// Removing a mod is the one destructive thing AIM does to a folder the user assembled by hand, so
/// it is routed through the shell rather than <see cref="Directory.Delete(string, bool)"/>: a
/// mistake stays recoverable without AIM having to invent its own trash folder and then explain
/// where it went.
///
/// Only Windows is wired up. AIM builds for Linux and macOS too, and there the caller is expected
/// to check <see cref="IsSupported"/> and warn that the delete is permanent before doing it.
/// </summary>
public static class RecycleBin
{
    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>
    /// Sends <paramref name="path"/> - a file or a whole directory - to the Recycle Bin.
    /// </summary>
    /// <returns>False when the platform has no recycle bin, or the shell refused the operation.</returns>
    public static bool TryDelete(string path)
    {
        if (!IsSupported || string.IsNullOrWhiteSpace(path)) return false;
        if (!File.Exists(path) && !Directory.Exists(path)) return false;

        // SHFileOperation is a shell call, and the shell expects a single-threaded apartment. Called
        // from the thread pool - which is where removing a mod runs it, to keep the UI alive - it
        // reaches shell extensions initialised for the wrong apartment and can take the process down
        // with an access violation that no catch block here can see. Its own STA thread costs a few
        // milliseconds and makes the call one the shell actually supports.
        return RunOnStaThread(() => Delete(path));
    }

    // ── Threading ────────────────────────────────────────────────────────────────

    private static bool RunOnStaThread(Func<bool> work)
    {
        var result = false;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        try
        {
            // TrySetApartmentState rather than SetApartmentState: on a platform where STA is not a
            // thing the delete should still be attempted rather than throwing before it starts.
            thread.TrySetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not run the recycle bin delete: {exception.Message}");
            return false;
        }

        if (failure is null) return result;

        Logger.Log($"Recycle bin delete failed: {failure.Message}");
        return false;
    }

    private static bool Delete(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);

            // SHFileOperation takes a list of paths in one string, so the list itself is terminated
            // by a second NUL on top of the one that ends the last entry.
            var operation = new ShFileOpStruct
            {
                wFunc = FoDelete,
                pFrom = full + '\0' + '\0',
                fFlags = FofAllowUndo | FofNoConfirmation | FofNoErrorUi | FofSilent | FofNoConfirmMkDir
            };

            var result = SHFileOperationW(ref operation);
            if (result != 0 || operation.fAnyOperationsAborted)
            {
                Logger.Log($"The shell refused to recycle {path} (code {result}).");
                return false;
            }

            // A success code with the path still present is worth a line in the log - it usually
            // means an antivirus or a handle held by the game got in the way - but the shell is
            // still the authority on whether the operation happened, so it is not called a failure.
            if (File.Exists(full) || Directory.Exists(full))
                Logger.Log($"The shell reported {path} recycled, but it is still on disk.");

            return true;
        }
        catch (Exception exception)
        {
            Logger.Log($"Recycle bin delete failed for {path}: {exception.Message}");
            return false;
        }
    }

    // ── shell32 interop ──────────────────────────────────────────────────────────

    private const uint FoDelete = 0x0003;
    private const ushort FofSilent = 0x0004;
    private const ushort FofNoConfirmation = 0x0010;
    private const ushort FofAllowUndo = 0x0040;
    private const ushort FofNoConfirmMkDir = 0x0200;
    private const ushort FofNoErrorUi = 0x0400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHFileOperationW(ref ShFileOpStruct fileOp);
}
