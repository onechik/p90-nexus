using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace P90.BootstrapProbe;

// Stage 2 diagnostic only. Does not create a lobby or modify gameplay state.
[BepInPlugin("onechik.p90.bootstrap-probe", "P90 Bootstrap Probe", "0.1.0")]
[BepInProcess("SCP Project 90.exe")]
public sealed class BootstrapProbe : BasePlugin
{
    internal static BootstrapProbe Instance { get; private set; } = null!;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly object logLock = new();
    private string logPath = "";
    private bool quitting;
    internal double ExitAfterSeconds { get; private set; }
    internal double ElapsedSeconds => clock.Elapsed.TotalSeconds;

    public override void Load()
    {
        Instance = this;
        var runId = $"{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}-{Environment.ProcessId}";
        var folder = Path.Combine(Paths.GameRootPath, "research", "stage02", "runs", runId);
        Directory.CreateDirectory(folder);
        logPath = Path.Combine(folder, "probe.jsonl");
        ExitAfterSeconds = ParseExitArgument(Environment.GetCommandLineArgs());
        Write("managed_loaded", new
        {
            version = "0.1.0", runId, processId = Environment.ProcessId,
            runtime = RuntimeInformation.FrameworkDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            mainThread = Environment.CurrentManagedThreadId,
            autoExitSeconds = ExitAfterSeconds
        });
        try
        {
            var domain = IL2CPP.il2cpp_domain_get();
            var manager = IL2CPP.GetIl2CppClass("Assembly-CSharp.dll", "", "PluginManager");
            var round = IL2CPP.GetIl2CppClass("Assembly-CSharp.dll", "", "RoundManager");
            var server = IL2CPP.GetIl2CppClass("Mirror.dll", "Mirror", "NetworkServer");
            if (domain == IntPtr.Zero || manager == IntPtr.Zero || round == IntPtr.Zero || server == IntPtr.Zero)
                throw new InvalidOperationException("Required IL2CPP domain or game class was not resolved.");
            Write("interop_ready", new
            {
                unity = Application.unityVersion, product = Application.productName,
                batchMode = Application.isBatchMode,
                gameClass = "PluginManager", roundClass = "RoundManager", networkClass = "Mirror.NetworkServer"
            });
            AddComponent<ProbeBehaviour>();
            Write("component_registered", new { type = nameof(ProbeBehaviour) });
        }
        catch (Exception error)
        {
            Write("probe_failed", new { error = error.ToString() });
            throw;
        }
    }

    internal void Write(string kind, object details)
    {
        var line = JsonSerializer.Serialize(new
        {
            utc = DateTime.UtcNow.ToString("O"), elapsedSeconds = Math.Round(ElapsedSeconds, 3),
            kind, details
        });
        lock (logLock) File.AppendAllText(logPath, line + Environment.NewLine);
        Log.LogInfo(line);
    }

    internal void RequestDiagnosticExit()
    {
        if (quitting) return;
        quitting = true;
        Write("quit_requested", new { reason = "explicit diagnostic command-line argument" });
        Application.Quit(0);
    }

    private static double ParseExitArgument(string[] args)
    {
        const string prefix = "--p90-probe-exit-seconds=";
        foreach (var arg in args)
            if (arg.StartsWith(prefix, StringComparison.Ordinal)
                && double.TryParse(arg[prefix.Length..], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var seconds)
                && seconds >= 5 && seconds <= 300)
                return seconds;
        return 0; // Never terminate an ordinary game session automatically.
    }
}

public sealed class ProbeBehaviour : MonoBehaviour
{
    private int frames;
    private double nextReport;
    private bool failed;
    public ProbeBehaviour(IntPtr pointer) : base(pointer) { }

    public void Update()
    {
        if (failed) return;
        var probe = BootstrapProbe.Instance;
        try
        {
            frames++;
            if (frames == 1 || probe.ElapsedSeconds >= nextReport)
            {
                var scene = SceneManager.GetActiveScene();
                probe.Write("unity_update", new
                {
                    frames, unityFrame = Time.frameCount, scene = scene.name,
                    sceneLoaded = scene.isLoaded, thread = Environment.CurrentManagedThreadId
                });
                nextReport = probe.ElapsedSeconds + 5;
            }
            if (probe.ExitAfterSeconds > 0 && probe.ElapsedSeconds >= probe.ExitAfterSeconds)
                probe.RequestDiagnosticExit();
        }
        catch (Exception error)
        {
            failed = true;
            probe.Write("probe_failed", new { error = error.ToString(), frames });
        }
    }

    public void OnApplicationQuit() => BootstrapProbe.Instance.Write("application_quit", new { frames });
}
