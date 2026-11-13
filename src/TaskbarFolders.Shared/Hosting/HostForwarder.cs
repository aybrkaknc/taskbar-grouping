using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace TaskbarFolders.Shared.Hosting;

/// <summary>
/// Sends a <see cref="HostRequest"/> to the resident launcher. Used by every process the shell
/// starts for a click while a host is running — the launcher itself, and the native click client —
/// which forwards the click and exits instead of building a popup of its own. It lives in Shared so
/// that both senders speak the protocol with the same code; it uses no reflection, so a native-AOT
/// client can call it.
/// </summary>
public static class HostForwarder
{
    /// <summary>How long to wait for the host to take the pipe connection.</summary>
    public const int ConnectTimeoutMs = 100;

    /// <summary>
    /// How long to wait for the host to acknowledge the request. A host that does not answer is
    /// treated as absent, so a hung host costs one slow click, not every click.
    /// </summary>
    public static readonly TimeSpan AckTimeout = TimeSpan.FromMilliseconds(400);

    /// <summary>Sends <paramref name="request"/> to the host.</summary>
    /// <param name="request">The request.</param>
    /// <returns><see langword="true"/> when a host took and acknowledged it; <see langword="false"/> when there is no host or it did not answer.</returns>
    public static bool TryForward(HostRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!HostEndpoint.HostExists())
        {
            return false;
        }

        try
        {
            using var client = new NamedPipeClientStream(".", HostEndpoint.PipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            client.Connect(ConnectTimeoutMs);

            // The popup opens in the host, which did not receive this click, so Windows would not
            // let it come to the front. This process did: hand the right over before sending.
            if (GetNamedPipeServerProcessId(client.SafePipeHandle.DangerousGetHandle(), out var hostPid))
            {
                _ = AllowSetForegroundWindow(hostPid);
            }

            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            using var writer = new StreamWriter(client, encoding, bufferSize: 1024, leaveOpen: true) { AutoFlush = true };
            writer.WriteLine(request.ToLine());

            using var reader = new StreamReader(client, encoding, detectEncodingFromByteOrderMarks: false, bufferSize: 64, leaveOpen: true);
            var ack = Task.Run(reader.ReadLine);
            return ack.Wait(AckTimeout) && string.Equals(ack.Result, "ok", StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException or AggregateException)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out uint serverProcessId);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
