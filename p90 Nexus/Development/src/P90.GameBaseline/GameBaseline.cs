using System.Diagnostics;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace P90.GameBaseline;

// Temporary comparison module. No references to P90.API/Core/GameAdapter or their plugins.
[BepInPlugin("onechik.p90.game-baseline", "P90 game baseline (temporary)", "0.1.0")]
public sealed class GameBaseline : BasePlugin
{
    internal static GameBaseline Instance = null!;
    public override void Load()
    {
        Instance = this;
        if (!Environment.GetCommandLineArgs().Contains("--p90-game-baseline")) return;
        Log.LogInfo("BASELINE: only vanilla local host startup; no P90 core/adapter/plugins and no broadcast calls.");
        AddComponent<BaselineBehaviour>();
    }
    internal void Report(string text) => Log.LogInfo(text);
}
public sealed class BaselineBehaviour : MonoBehaviour
{
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private int phase;
    private double since;
    public BaselineBehaviour(IntPtr pointer) : base(pointer) { }
    public void Update()
    {
        try
        {
            if (clock.Elapsed.TotalSeconds > 25) { Application.Quit(2); return; }
            if (phase == 0 && clock.Elapsed.TotalSeconds > 3 && SceneManager.GetActiveScene().name == "MainMenu")
            {
                NetworkManager.lnh.onlineScene = "Sandbox"; NetworkServer.lom = false;
                NetworkManager.lnh.gsr(); phase = 1; since = clock.Elapsed.TotalSeconds;
                GameBaseline.Instance.Report("BASELINE_HOST_STARTED");
            }
            if (phase == 1 && clock.Elapsed.TotalSeconds - since > 5 && SceneManager.GetActiveScene().name == "Sandbox")
            {
                GameBaseline.Instance.Report("BASELINE_SANDBOX_OBSERVED");
                NetworkManager.lnh.gst(); phase = 2; since = clock.Elapsed.TotalSeconds;
            }
            if (phase == 2 && clock.Elapsed.TotalSeconds - since > 3 && SceneManager.GetActiveScene().name == "MainMenu")
            {
                phase = 3; GameBaseline.Instance.Report("BASELINE_DONE"); Application.Quit(0);
            }
        }
        catch (Exception error) { GameBaseline.Instance.Report("BASELINE_FAILED " + error); Application.Quit(1); }
    }
}
