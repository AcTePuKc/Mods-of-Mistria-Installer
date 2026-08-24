using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace Garethp.ModsOfMistriaInstallerLib.Nexus;

/// <summary>
/// The hand-off between the copy of AIM the browser launches for an nxm:// link and the copy the
/// user already has open.
///
/// The OS starts a brand new process for every clicked link. Without this, each click would open
/// another window; with it, the new process passes the link down a named pipe and exits, and the
/// running window picks the download up. It is the same trick Vortex and MO2 use.
///
/// .NET implements named pipes on Linux as Unix domain sockets, so one implementation covers both
/// platforms. The pipe name is derived from the user name so that two people logged into the same
/// machine do not steal each other's downloads.
/// </summary>
public sealed class NxmLinkListener : IDisposable
{
    private readonly Action<string> _onLinkReceived;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _loop;

    private NxmLinkListener(Action<string> onLinkReceived) => _onLinkReceived = onLinkReceived;

    public static string PipeName
    {
        get
        {
            var user = Environment.UserName ?? "user";
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user)))[..12];
            return $"AIM-nxm-{hash}";
        }
    }

    /// <summary>
    /// Tries to hand a link to an already-running AIM. Returns false when nothing is listening,
    /// which means this process should carry on and become the window that handles it.
    /// </summary>
    public static bool TrySend(string link, int timeoutMilliseconds = 1500)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(timeoutMilliseconds);

            using var writer = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true };
            writer.WriteLine(link);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (Exception e)
        {
            Logger.Log($"Could not pass the download link to the running installer: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Starts listening for links from other AIM processes. Returns null when another instance is
    /// already listening - that is expected, not an error, and simply means this process is not
    /// the one that owns the pipe.
    /// </summary>
    public static NxmLinkListener? TryStart(Action<string> onLinkReceived)
    {
        var listener = new NxmLinkListener(onLinkReceived);

        try
        {
            // Bind once up front so an "already in use" failure is reported here rather than
            // disappearing inside the background loop.
            var first = CreateServer();
            listener._loop = Task.Run(() => listener.RunAsync(first));
            return listener;
        }
        catch (Exception e)
        {
            Logger.Log($"Not listening for nxm:// links in this process: {e.Message}");
            listener.Dispose();
            return null;
        }
    }

    private static NamedPipeServerStream CreateServer() =>
        new(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

    private async Task RunAsync(NamedPipeServerStream first)
    {
        var server = first;

        try
        {
            while (!_cancellation.IsCancellationRequested)
            {
                try
                {
                    await server.WaitForConnectionAsync(_cancellation.Token);

                    using (var reader = new StreamReader(server, new UTF8Encoding(false), false, 1024, true))
                    {
                        var link = (await reader.ReadLineAsync(_cancellation.Token))?.Trim();
                        if (!string.IsNullOrEmpty(link)) _onLinkReceived(link);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception e)
                {
                    Logger.Log($"Failed to read an incoming nxm:// link: {e.Message}");
                }
                finally
                {
                    await server.DisposeAsync();
                }

                if (_cancellation.IsCancellationRequested) break;
                server = CreateServer();
            }
        }
        finally
        {
            await server.DisposeAsync();
        }
    }

    public void Dispose()
    {
        if (_cancellation.IsCancellationRequested) return;

        _cancellation.Cancel();

        // A server stream parked in WaitForConnectionAsync only wakes up when something connects,
        // so a throwaway connection is used to unblock it before the process exits.
        try
        {
            using var poke = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            poke.Connect(200);
        }
        catch
        {
            // Nothing listening any more, which is the state we wanted.
        }

        var stopped = false;
        try
        {
            stopped = _loop?.Wait(TimeSpan.FromSeconds(2)) ?? true;
        }
        catch
        {
            // Shutdown is best effort.
        }

        // The loop still reads the token if it did not stop in time; disposing the source under it
        // would raise an unobserved exception on a thread-pool thread.
        if (stopped) _cancellation.Dispose();
    }
}
