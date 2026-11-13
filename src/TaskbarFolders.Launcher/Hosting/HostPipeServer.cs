using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TaskbarFolders.Shared.Hosting;

namespace TaskbarFolders.Launcher.Hosting;

/// <summary>
/// The resident launcher's end of the pipe. It accepts one connection at a time but keeps the next
/// server instance ready before it handles the current request, so a click that arrives while the
/// previous one is being served never finds the pipe missing. Each connection carries one line (a
/// <see cref="HostRequest"/>); the server answers <c>ok</c> as soon as the line parses, then hands
/// the request on, so the client can exit without waiting for the popup to open.
/// </summary>
public sealed class HostPipeServer : IDisposable
{
    private static readonly TimeSpan _readTimeout = TimeSpan.FromSeconds(2);

    private readonly string _pipeName;
    private readonly Func<HostRequest, Task> _handler;
    private readonly ILogger? _logger;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>Initializes a new instance.</summary>
    /// <param name="pipeName">The pipe name; <see cref="HostEndpoint.PipeName"/> in production.</param>
    /// <param name="handler">Called, off the caller's thread, for each valid request.</param>
    /// <param name="logger">Optional logger.</param>
    public HostPipeServer(string pipeName, Func<HostRequest, Task> handler, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(pipeName);
        ArgumentNullException.ThrowIfNull(handler);

        _pipeName = pipeName;
        _handler = handler;
        _logger = logger;
    }

    /// <summary>Starts accepting connections on a background task.</summary>
    public void Start() => _ = Task.Run(() => AcceptLoopAsync(_cts.Token));

    /// <inheritdoc/>
    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }

    private NamedPipeServerStream CreateServer() => new(
        _pipeName,
        PipeDirection.InOut,
        NamedPipeServerStream.MaxAllowedServerInstances,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = CreateServer();
                await server.WaitForConnectionAsync(token).ConfigureAwait(false);

                var connected = server;
                server = null;
                _ = Task.Run(() => ServeAsync(connected, token), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A client that vanished mid-connect, or a pipe that could not be created. Log and
                // try again shortly rather than ending the host's only way to hear a click.
                _logger?.LogWarning(ex, "Host pipe accept failed; retrying.");
                await Task.Delay(TimeSpan.FromMilliseconds(250), CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                server?.Dispose();
            }
        }
    }

    private async Task ServeAsync(NamedPipeServerStream server, CancellationToken token)
    {
        using (server)
        {
            try
            {
                using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                readTimeout.CancelAfter(_readTimeout);

                var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                using var reader = new StreamReader(server, encoding, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
                var line = await reader.ReadLineAsync(readTimeout.Token).ConfigureAwait(false);

                using var writer = new StreamWriter(server, encoding, bufferSize: 16, leaveOpen: true) { AutoFlush = true };
                if (!HostRequest.TryParse(line, out var request))
                {
                    _logger?.LogWarning("Host pipe received a malformed request; ignored.");
                    await writer.WriteLineAsync("bad").ConfigureAwait(false);
                    return;
                }

                await writer.WriteLineAsync("ok").ConfigureAwait(false);
                await _handler(request).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Host shutting down, or a client that connected and never sent its line.
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                _logger?.LogWarning(ex, "Host pipe connection failed.");
            }
        }
    }
}
