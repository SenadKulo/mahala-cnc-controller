using System.Collections.Concurrent;
using System.Text.Json;

namespace CncController.App.Tests;

/// <summary><see cref="LocalPage"/>: the desktop window's page, driven by JSON messages as WebView2 would pass them.</summary>
public sealed class LocalPageTests : IDisposable
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    readonly Dispatcher _ui = new();
    readonly MachineSession _session;
    readonly LocalPage _page;
    readonly ConcurrentQueue<JsonElement> _sent = new();
    readonly ConcurrentQueue<string> _log = new();

    public LocalPageTests()
    {
        _session = new MachineSession(new AppSettings(), new PositionMemory(), logFolder: null, _ui.Post);
        _session.Logged += (_, text) => _log.Enqueue(text);
        var api = new PageApi(_session, _ui.Post, Path.Combine(Path.GetTempPath(), $"local-page-{Guid.NewGuid():N}"));
        _page = new LocalPage(api, _ui.Post, json => _sent.Enqueue(JsonDocument.Parse(json).RootElement));
    }

    public void Dispose()
    {
        _ui.Invoke(_page.Dispose);
        _ui.Invoke(_session.Dispose);
        _ui.Dispose();
    }

    void Receive(object message) => _ui.Invoke(() => _page.Receive(JsonSerializer.Serialize(message)));

    JsonElement WaitFor(Func<JsonElement, bool> match)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            foreach (var message in _sent)
                if (match(message))
                    return message;
            Thread.Sleep(20);
        }
        throw new TimeoutException("Sent: " + string.Join(Environment.NewLine, _sent.Select(m => m.GetRawText()[..Math.Min(200, m.GetRawText().Length)])));
    }

    static bool IsEvent(JsonElement m, string name) => m.GetProperty("type").GetString() == "event" && m.GetProperty("name").GetString() == name;

    static bool IsReply(JsonElement m, int id) => m.GetProperty("type").GetString() == "reply" && m.GetProperty("id").GetInt32() == id;

    [Fact]
    public void Ready_SendsEverything_AndTheWindowsOwnState()
    {
        _ui.Invoke(() => _page.SetHost(new { remoteText = "Daljinski pristup: isključen" }));

        Receive(new { type = "ready" });

        Assert.True(WaitFor(m => IsEvent(m, "Desktop")).GetProperty("args")[0].GetProperty("online").GetBoolean());
        WaitFor(m => IsEvent(m, "Toolpath"));
        Assert.True(WaitFor(m => IsEvent(m, "Log")).GetProperty("args")[1].GetBoolean());
        Assert.Equal("NIJE SPOJENO", WaitFor(m => IsEvent(m, "Snapshot")).GetProperty("args")[0].GetProperty("stateText").GetString());
        Assert.Equal("Daljinski pristup: isključen", WaitFor(m => IsEvent(m, "Host")).GetProperty("args")[0].GetProperty("remoteText").GetString());
    }

    [Fact]
    public void Command_RunsOnTheSession_WithoutAWebLogLine()
    {
        Receive(new { type = "ready" });

        Receive(new { type = "invoke", id = 7, method = "Command", args = new object[] { new { name = "Connect", text = MachineSession.SimulatorPortName } } });

        var reply = WaitFor(m => IsReply(m, 7));
        Assert.True(reply.GetProperty("ok").GetBoolean());
        Assert.True(reply.GetProperty("result").GetProperty("done").GetBoolean());
        Assert.True(_ui.Invoke(() => _session.IsOpen));
        // Like a click in the window: the log tells what the session did, not "Sa weba".
        Assert.DoesNotContain(_log, l => l.StartsWith(RemoteLink.Origin));
        // And the page follows by itself.
        WaitFor(m => IsEvent(m, "Snapshot") && m.GetProperty("args")[0].GetProperty("canDisconnect").GetBoolean());
    }

    [Fact]
    public void HostMethod_AndUnknownMethod_AreAnswered()
    {
        int opened = 0;
        _ui.Invoke(() => _page.AddHostMethod("OpenFile", _ => { opened++; return null; }));

        Receive(new { type = "invoke", id = 1, method = "OpenFile", args = Array.Empty<object>() });
        Receive(new { type = "invoke", id = 2, method = "FormatC", args = Array.Empty<object>() });

        Assert.True(WaitFor(m => IsReply(m, 1)).GetProperty("ok").GetBoolean());
        Assert.Equal(1, opened);
        Assert.False(WaitFor(m => IsReply(m, 2)).GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void BeforeReady_NothingIsSent()
    {
        _ui.Invoke(() => _session.Log("prije stranice"));
        Thread.Sleep(500);

        Assert.Empty(_sent);
        Receive(new { type = "ready" });
        // The recent log still has it.
        Assert.Contains("prije stranice", WaitFor(m => IsEvent(m, "Log")).GetRawText());
    }
}
