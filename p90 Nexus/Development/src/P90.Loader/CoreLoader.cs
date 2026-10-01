using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using P90.Core;
using P90.API;
using P90.GameAdapter;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace P90.Loader;

[BepInPlugin("onechik.p90.loader", "P90 Core", "0.3.0")]
[BepInProcess("SCP Project 90.exe")]
public sealed class CoreLoader : BasePlugin
{
    internal static CoreLoader Instance { get; private set; } = null!;
    internal PluginHost Host { get; private set; } = null!;
    internal bool SelfTest { get; private set; }
    internal bool AdapterSelfTest { get; private set; }
    internal GameServer Server { get; private set; } = null!;
    internal LocalControl Control { get; private set; } = null!;
    internal AdapterTest? GameTest { get; private set; }
    internal double ExitAfterSeconds { get; private set; }
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private string logPath = "";
    internal double Elapsed => clock.Elapsed.TotalSeconds;

    public override void Load()
    {
        Instance = this;
        var root = Path.Combine(Paths.GameRootPath, "P90");
        var folder = Path.Combine(root, "logs", "runs", $"{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}-{Environment.ProcessId}");
        Directory.CreateDirectory(folder);
        logPath = Path.Combine(folder, "core.jsonl");
        foreach (var arg in Environment.GetCommandLineArgs())
        {
            if (arg == "--p90-core-selftest") SelfTest = true;
            if (arg == "--p90-adapter-selftest") AdapterSelfTest = true;
            const string prefix = "--p90-core-exit-seconds=";
            if (arg.StartsWith(prefix, StringComparison.Ordinal) && double.TryParse(arg[prefix.Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds is >= 5 and <= 300)
                ExitAfterSeconds = seconds;
        }
        var buildError = BuildGuard.Verify(Paths.GameRootPath);
        Server = new GameServer(buildError);
        Write("adapter_build_check", new { supported = buildError.Length == 0, reason = buildError });
        Host = new PluginHost(root, entry =>
        {
            var text = $"[{entry.PluginId}] {entry.Message}";
            if (entry.Level == "Error") Log.LogError(text); else Log.LogInfo(text);
        }, Server);
        try
        {
            var results = Host.DiscoverAndLoad();
            Control = new LocalControl(Host);
            if (AdapterSelfTest) GameTest = new AdapterTest(this);
            Write("core_loaded", new { processId = Environment.ProcessId, thread = Environment.CurrentManagedThreadId, selfTest = SelfTest, results, plugins = Host.Plugins });
            AddComponent<CoreBehaviour>();
        }
        catch
        {
            Control?.Dispose();
            Host.StopAll();
            throw;
        }
    }

    internal void PollGame()
    {
        try { foreach (var entry in Server.Poll()) PublishGameEvent(entry); }
        catch (Exception error)
        {
            Write("adapter_failed", new { error = error.ToString() });
            foreach (var entry in Server.Disable("Game adapter failed; see core log.")) PublishGameEvent(entry);
        }
    }
    internal void PublishGameEvent(object entry)
    {
        switch (entry)
        {
            case ServerStarted e: Host.Publish(e); break;
            case ServerStopped e: Host.Publish(e); break;
            case PlayerAvailable e: Host.Publish(e); break;
            case PlayerUnavailable e: Host.Publish(e); break;
        }
        Write("game_event", new { type = entry.GetType().Name, value = entry });
    }

    internal void Write(string kind, object details)
    {
        var line = JsonSerializer.Serialize(new { utc = DateTime.UtcNow, kind, elapsed = Elapsed, details });
        File.AppendAllText(logPath, line + Environment.NewLine);
        Log.LogInfo(line);
    }

    internal void RunSelfTest()
    {
        void Check(string name, bool passed)
        {
            Write("selftest_check", new { name, passed, thread = Environment.CurrentManagedThreadId });
            if (!passed) throw new InvalidOperationException("Core self-test failed: " + name);
        }
        int Pulses(string command) => int.Parse(Host.ExecuteLocal(command).Message, CultureInfo.InvariantCulture);
        Check("three-active-plugins", Host.Plugins.Count == 3 && Host.Plugins.All(p => p.State == PluginState.Active));
        Check("initial-registrations", Host.CommandCount == 9 && Host.SubscriptionCount == 6);
        Check("local-command", Host.ExecuteLocal("sample.hello").Success);
        Check("duplicate-id-rejected", Host.DiscoverAndLoad() is { Count: 3 } duplicates && duplicates.All(r => !r.Success) && Host.CommandCount == 9);
        var errors = Host.HandlerErrors;
        var pulses = Pulses("sample.pulses");
        Host.ExecuteLocal("diagnostic.arm"); Host.Tick();
        Check("event-exception-contained", Host.HandlerErrors == errors + 1 && Pulses("sample.pulses") == pulses + 1);
        Check("command-exception-contained", !Host.ExecuteLocal("diagnostic.fail").Success && Host.HandlerErrors == errors + 2 && Host.ExecuteLocal("sample.hello").Success);
        Check("stop-removes-resources", Host.Stop("sample.lifecycle").Success && Host.CommandCount == 7 && Host.SubscriptionCount == 5 && !Host.ExecuteLocal("sample.hello").Success);
        var diagnosticPulses = Pulses("diagnostic.status"); Host.Tick();
        Check("other-plugin-keeps-running", Pulses("diagnostic.status") == diagnosticPulses + 1);
        Check("restart-without-duplicates", Host.Start("sample.lifecycle").Success && !Host.Start("sample.lifecycle").Success && Host.CommandCount == 9 && Host.SubscriptionCount == 6);
        Host.StopAll(); Host.StopAll();
        Check("idempotent-clean-shutdown", Host.CommandCount == 0 && Host.SubscriptionCount == 0 && Host.Plugins.All(p => p.State == PluginState.Stopped));
        Check("restart-for-quit-test", Host.Start("sample.diagnostics").Success && Host.Start("sample.lifecycle").Success && Host.Start("sample.server").Success);
        Write("core_selftest_passed", new { checks = 11, handlerErrors = Host.HandlerErrors });
    }
}

public sealed class CoreBehaviour : MonoBehaviour
{
    private double nextPulse;
    private bool tested, failed, quitting;
    private double nextControl;
    public CoreBehaviour(IntPtr pointer) : base(pointer) { }
    public void Update()
    {
        var loader = CoreLoader.Instance;
        try
        {
            if (!failed)
            {
                loader.PollGame();
                if (loader.Elapsed >= nextControl) { loader.Control.Poll(); nextControl = loader.Elapsed + 0.1; }
                loader.GameTest?.Update();
            }
            if (!failed && loader.Elapsed >= nextPulse)
            {
                loader.Host.Tick();
                nextPulse = loader.Elapsed + 1;
                // Pulse is a core diagnostic event, not a server/gameplay event.
                if (loader.SelfTest) loader.Write("unity_update", new { scene = SceneManager.GetActiveScene().name, thread = Environment.CurrentManagedThreadId });
            }
            if (!failed && loader.SelfTest && !tested && SceneManager.GetActiveScene().name == "MainMenu")
            {
                tested = true;
                loader.RunSelfTest();
            }
        }
        catch (Exception error)
        {
            failed = true;
            loader.Write("core_failed", new { error = error.ToString() });
            foreach (var entry in loader.Server.Disable("Adapter/update failed; see core log.")) loader.PublishGameEvent(entry);
            loader.Control.Dispose();
            loader.Host.StopAll();
            if (loader.AdapterSelfTest || loader.SelfTest) Application.Quit(1);
        }
        if (!quitting && loader.ExitAfterSeconds > 0 && loader.Elapsed >= loader.ExitAfterSeconds)
        {
            quitting = true;
            loader.Write("quit_requested", new { tested, failed });
            Application.Quit(0);
        }
    }
    public void OnApplicationQuit()
    {
        var loader = CoreLoader.Instance;
        foreach (var entry in loader.Server.Disable("Application quit.")) loader.PublishGameEvent(entry);
        loader.Control.Dispose();
        loader.Host.StopAll();
        loader.Write("application_quit", new { commands = loader.Host.CommandCount, subscriptions = loader.Host.SubscriptionCount, plugins = loader.Host.Plugins, thread = Environment.CurrentManagedThreadId });
    }
}
