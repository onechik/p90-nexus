using Mirror;
using P90.API;
using P90.Core;
using UnityEngine.SceneManagement;

namespace P90.GameAdapter;

public sealed class GameServer : IGameServer
{
    private readonly int thread = Environment.CurrentManagedThreadId;
    private readonly ServerTracker tracker = new();
    private string disabledReason;
    public GameServer(string buildError) => disabledReason = buildError;
    private void Guard() { if (thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Game API requires the Unity thread."); }
    public ServerSnapshot Snapshot
    {
        get
        {
            Guard();
            if (disabledReason.Length != 0) return new(false, false, false, "", "", Array.Empty<PlayerSnapshot>(), disabledReason);
            // A cached player list is observational. Authority is always rechecked live.
            if (!NetworkServer.lon) return tracker.Current with { IsServer = false, IsClient = NetworkClient.njo, Players = Array.Empty<PlayerSnapshot>(), SessionId = "" };
            return tracker.Current;
        }
    }
    public IReadOnlyList<object> Poll()
    {
        Guard();
        if (disabledReason.Length != 0) return Array.Empty<object>();
        var players = new List<PlayerSnapshot>();
        if (NetworkServer.lon)
        {
            foreach (var pair in NetworkServer.loj)
            {
                var connection = pair.Value;
                var identity = connection.llq;
                if (!connection.llo || identity == null) continue;
                var stats = identity.GetComponent<PlayerStatistics>();
                if (stats == null) continue;
                players.Add(new(pair.Key, identity.lmq, stats.myNick ?? ""));
            }
        }
        return tracker.Observe(NetworkServer.lon, NetworkClient.njo, SceneManager.GetActiveScene().name, players);
    }
    public IReadOnlyList<object> Disable(string reason)
    {
        Guard(); disabledReason = reason;
        return tracker.Observe(false, false, "", Array.Empty<PlayerSnapshot>());
    }
    public CommandResult Broadcast(string text)
    {
        Guard();
        if (disabledReason.Length != 0) return CommandResult.Fail(disabledReason);
        if (!NetworkServer.lon) return CommandResult.Fail("Only the active host/server can broadcast.");
        if (text is null || text.Length > 512 || text.Any(c => c is '<' or '>' || (char.IsControl(c) && c != '\n')))
            return CommandResult.Fail("Use at most 512 plain-text characters; markup/control characters are not allowed.");
        var ready = new List<gu>();
        foreach (var pair in NetworkServer.loj) if (pair.Value.llo && pair.Value.llq != null) ready.Add(pair.Value);
        if (ready.Count == 0) return CommandResult.Fail("No ready players.");
        // One existing identity observed by every ready player; send exactly ONE RPC.
        // Never broadcast once per player: each call itself fans out to observers.
        foreach (var connection in ready)
        {
            var identity = connection.llq;
            var admin = identity.GetComponent<PlayerAdmin>();
            if (admin == null || !ready.All(c => identity.observers.ContainsKey(c.llx))) continue;
            admin.cfh(text); // Original RpcDoBroadcast wrapper, verified RVA 0x340A40.
            return CommandResult.Ok($"Queued one game broadcast RPC for {ready.Count} ready observer(s).");
        }
        return CommandResult.Fail("No shared PlayerAdmin identity is visible to all ready players; try again after loading.");
    }
}
