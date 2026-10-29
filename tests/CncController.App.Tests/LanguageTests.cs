using System.Collections.Concurrent;
using System.Globalization;
using CncController.Remote;
using GrblCore;

namespace CncController.App.Tests;

/// <summary>The program's language: chosen on the page, kept in the settings, and spoken by the session and the page's texts.</summary>
public sealed class LanguageTests : IDisposable
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    readonly Dispatcher _ui = new();
    readonly AppSettings _settings = new();
    readonly MachineSession _session;
    readonly PageApi _api;
    readonly ConcurrentQueue<string> _log = new();

    public LanguageTests()
    {
        _session = new MachineSession(_settings, new PositionMemory(), logFolder: null, _ui.Post);
        _session.Logged += (_, text) => _log.Enqueue(text);
        _api = new PageApi(_session, _ui.Post, Path.Combine(Path.GetTempPath(), $"language-{Guid.NewGuid():N}"));
    }

    public void Dispose()
    {
        _ui.Invoke(_session.Dispose);
        _ui.Dispose();
    }

    void WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!_ui.Invoke(condition))
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Log:" + Environment.NewLine + string.Join(Environment.NewLine, _log));
            Thread.Sleep(20);
        }
    }

    [Fact]
    public void NewSettings_AreBosnian()
    {
        Assert.Equal(Language.Bosnian, new AppSettings().Language);
        Assert.Equal("bs", _ui.Invoke(_api.BuildSnapshot).Language);
        Assert.Equal("NIJE SPOJENO", _ui.Invoke(_api.BuildSnapshot).StateText);
    }

    [Fact]
    public void LanguageCommand_SwitchesTheSessionAndThePage()
    {
        var reply = _ui.Invoke(() => _api.Execute(new RemoteCommand { Name = RemoteCommands.Language, Text = "en" }, RemoteLink.Origin));

        Assert.True(reply.Done);
        Assert.Equal(Language.English, _settings.Language);
        var snapshot = _ui.Invoke(_api.BuildSnapshot);
        Assert.Equal("en", snapshot.Language);
        Assert.Equal("NOT CONNECTED", snapshot.StateText);
        Assert.Equal("Not connected", snapshot.ConnectionText);
        Assert.Contains(_log, l => l == "From the web: Language: English.");
    }

    [Fact]
    public void LanguageCommand_UnknownLanguage_IsRefused()
    {
        var reply = _ui.Invoke(() => _api.Execute(new RemoteCommand { Name = RemoteCommands.Language, Text = "de" }, null));

        Assert.False(reply.Done);
        Assert.Equal(Language.Bosnian, _settings.Language);
    }

    [Fact]
    public void English_SessionLogsInEnglish()
    {
        _ui.Invoke(() => _session.Language = Language.English);

        Assert.True(_ui.Invoke(() => _session.Connect(MachineSession.SimulatorPortName)));
        WaitUntil(() => _session.IsReady && _session.LastStatus is not null);

        Assert.Contains(_log, l => l == "Opened Simulator, waiting for Grbl's greeting...");
        Assert.Equal("IDLE", _ui.Invoke(() => _session.StateText));
        _ui.Invoke(_session.Disconnect);
        Assert.Contains(_log, l => l == "Disconnected.");
    }

    [Fact]
    public void Question_InBothLanguages()
    {
        var question = new PositionQuestion(new Position(1, 2, 0), new Position(10, 20, 0), new DateTime(2026, 10, 29, 14, 5, 0), @"C:\jobs\sign.tap", 120);

        var bosnian = Texts.Question(question, Language.Bosnian);
        var english = Texts.Question(question, Language.English);

        Assert.Contains("Posao sign.tap je stao na liniji 120.", bosnian);
        Assert.Contains("samo ako CNC u međuvremenu nije ručno pomjeran", bosnian);
        Assert.Contains("Job sign.tap stopped at line 120.", english);
        Assert.Contains("Answer Yes only if the CNC wasn't moved by hand", english);
    }

    [Fact]
    public void GrblErrorsAlarmsAndSettings_InBothLanguages()
    {
        Assert.Equal("error:22 (nedostaje brzina (F))", Texts.GrblError("error:22", Language.Bosnian));
        Assert.Equal("error:22 (feed rate (F) missing)", Texts.GrblError("error:22", Language.English));
        Assert.Equal("ALARM:1 (a limit switch was hit, the position is lost)", Texts.GrblAlarm("ALARM:1", Language.English));
        Assert.True(GrblVersion.TryParseBanner("Grbl 1.1h ['$' for help]", out var grbl11));
        Assert.Equal("Steps per mm, X axis", Texts.GrblSettingMeaning(grbl11, 100, Language.English));
        Assert.Equal("Koraka po mm, osa X", Texts.GrblSettingMeaning(grbl11, 100, Language.Bosnian));
    }

    [Theory]
    [InlineData("bs-Latn-BA", Language.Bosnian)]
    [InlineData("hr-HR", Language.Bosnian)]
    [InlineData("sr-Latn-RS", Language.Bosnian)]
    [InlineData("en-US", Language.English)]
    [InlineData("de-DE", Language.English)]
    public void FirstStart_FollowsWindows(string culture, Language expected) =>
        Assert.Equal(expected, Languages.FromCulture(CultureInfo.GetCultureInfo(culture)));

    [Fact]
    public void Settings_KeepTheLanguage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.json");
        try
        {
            var settings = AppSettings.Load(path);
            settings.Language = Language.English;
            settings.Save();

            Assert.Contains("\"English\"", File.ReadAllText(path));
            Assert.Equal(Language.English, AppSettings.Load(path).Language);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
