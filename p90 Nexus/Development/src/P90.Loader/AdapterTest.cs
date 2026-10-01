using Mirror;
using P90.API;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace P90.Loader;

// Explicit diagnostic flag only. No Steam lobby is created/published; server listen is off.
internal sealed class AdapterTest
{
    private readonly CoreLoader loader;
    private int phase, cycle, checks;
    private double since;
    private string expected = "", previousSession = "", session = "", savedScene = "";
    private bool savedListen;
    private NetworkManager? manager;
    internal AdapterTest(CoreLoader loader) { this.loader = loader; since = loader.Elapsed; }
    private void Check(string name, bool passed)
    {
        loader.Write("adapter_check", new { name, passed, cycle, thread = Environment.CurrentManagedThreadId });
        checks++;
        if (!passed) throw new InvalidOperationException("Adapter check failed: " + name);
    }
    private void Next(int value) { phase = value; since = loader.Elapsed; }
    internal void Update()
    {
        if (phase == 99) return;
        if (loader.Elapsed - since > 70) throw new TimeoutException("Adapter self-test phase " + phase);
        var state = loader.Server.Snapshot;
        switch (phase)
        {
            case 0:
                if (SceneManager.GetActiveScene().name != "MainMenu" || loader.Elapsed < 3) return;
                Check("supported-build", state.Supported);
                Check("menu-authority-denied", !state.IsServer && !loader.Host.ExecuteLocal("server.announce", "MUST NOT DISPLAY").Success);
                manager = NetworkManager.lnh;
                Check("network-manager-present", manager != null);
                savedScene = manager!.onlineScene; savedListen = NetworkServer.lom;
                StartHost();
                break;
            case 1:
                if (!state.IsServer || !state.IsClient || state.Players.Count == 0 || AdmintTools.instance == null || AdmintTools.instance.broadcastText == null || loader.Elapsed - since < 3) return;
                Check("host-player-available", state.Players.Count == 1 && state.Players[0].NetId != 0);
                Check("new-session-id", state.SessionId.Length != 0 && state.SessionId != previousSession);
                Check("local-only-listener", !NetworkServer.lom);
                session = state.SessionId;
                expected = "P90 stage 4 / session " + (cycle + 1);
                Check("rpc-queued", loader.Host.ExecuteLocal("server.announce", expected).Success);
                Next(2);
                break;
            case 2:
                if (AdmintTools.instance.broadcastText.text != expected) return;
                Check("rpc-reached-game-ui", true);
                loader.Write("broadcast_ui", new { text = AdmintTools.instance.broadcastText.text, active = AdmintTools.instance.broadcastText.gameObject.activeInHierarchy, scene = state.Scene });
                expected += " / repeat";
                Check("second-rpc-queued", loader.Host.ExecuteLocal("server.announce", expected).Success);
                Next(3);
                break;
            case 3:
                if (AdmintTools.instance.broadcastText.text != expected) return;
                Check("second-rpc-reached-ui", true);
                Check("clear-queued", loader.Host.ExecuteLocal("server.clear").Success);
                Next(4);
                break;
            case 4:
                if (AdmintTools.instance.broadcastText.text.Length != 0) return;
                Check("clear-reached-ui", true);
                manager!.gst(); // Original StopHost, RVA 0x12E66B0.
                Next(5);
                break;
            case 5:
                // Scene name switches before all Awake/Start/destruction work has completed.
                if (state.IsServer || SceneManager.GetActiveScene().name != "MainMenu" || loader.Elapsed - since < 3 || NetworkManager.lnh == null || NetworkManager.lnh.transport == null) return;
                Check("stop-clears-player-state", state.Players.Count == 0 && state.SessionId == "");
                Check("stop-revokes-authority", !loader.Host.ExecuteLocal("server.announce", "MUST NOT DISPLAY").Success);
                previousSession = session;
                cycle++;
                if (cycle < 2) { StartHost(); return; }
                NetworkManager.lnh.onlineScene = savedScene; NetworkServer.lom = savedListen;
                loader.Write("adapter_selftest_passed", new { checks, cycles = cycle });
                Next(99);
                Application.Quit(0);
                break;
        }
    }
    private void StartHost()
    {
        manager = NetworkManager.lnh; // A scene transition may replace the previous manager.
        manager!.onlineScene = "Sandbox";
        NetworkServer.lom = false;
        loader.Write("local_host_requested", new { scene = "Sandbox", cycle, listen = false });
        manager.gsr(); // Original StartHost, RVA 0x12E6210.
        Next(1);
    }
}
