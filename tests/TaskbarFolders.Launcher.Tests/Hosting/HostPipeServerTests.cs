using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using TaskbarFolders.Launcher.Hosting;
using TaskbarFolders.Shared.Hosting;
using Xunit;

namespace TaskbarFolders.Launcher.Tests.Hosting;

public sealed class HostPipeServerTests : IDisposable
{
    private readonly string _pipeName = "TaskbarFolders.Test.Pipe." + Guid.NewGuid().ToString("N");
    private readonly BlockingCollection<HostRequest> _received = [];
    private readonly HostPipeServer _server;

    public HostPipeServerTests()
    {
        _server = new HostPipeServer(
            _pipeName,
            request =>
            {
                _received.Add(request);
                return Task.CompletedTask;
            });
        _server.Start();
    }

    public void Dispose()
    {
        _server.Dispose();
        _received.Dispose();
    }

    private async Task<string?> SendAsync(string line)
    {
        using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(5000);

        var encoding = new UTF8Encoding(false);
        await using var writer = new StreamWriter(client, encoding, 1024, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(line);

        using var reader = new StreamReader(client, encoding, false, 64, leaveOpen: true);
        return await reader.ReadLineAsync();
    }

    [Fact]
    public async Task AValidRequest_IsAcknowledgedAndHandedToTheHandler()
    {
        var request = new HostRequest("g-1", 100, 200, [@"C:\a.exe"]);

        var ack = await SendAsync(request.ToLine());

        ack.Should().Be("ok");
        _received.TryTake(out var handled, TimeSpan.FromSeconds(5)).Should().BeTrue();
        handled.Should().BeEquivalentTo(request);
    }

    [Fact]
    public async Task AMalformedRequest_IsRejectedAndNotHandled()
    {
        var ack = await SendAsync("not a request");

        ack.Should().Be("bad");
        _received.TryTake(out _, TimeSpan.FromMilliseconds(300)).Should().BeFalse();
    }

    [Fact]
    public async Task SeveralClicksInARow_AreAllServed()
    {
        for (var i = 0; i < 5; i++)
        {
            (await SendAsync(new HostRequest("g", i, i, []).ToLine())).Should().Be("ok");
        }

        for (var i = 0; i < 5; i++)
        {
            _received.TryTake(out var handled, TimeSpan.FromSeconds(5)).Should().BeTrue();
            handled!.X.Should().Be(handled.Y);
        }
    }
}
