using P90.API;
namespace P90.Core;

public sealed class UnavailableGameServer : IGameServer
{
    public ServerSnapshot Snapshot => new(false, false, false, "", "", Array.Empty<PlayerSnapshot>(), "Game adapter is unavailable.");
    public CommandResult Broadcast(string text) => CommandResult.Fail(Snapshot.DisabledReason);
}

// Pure state transition logic. The adapter supplies a new snapshot on the Unity thread.
public sealed class ServerTracker
{
    public ServerSnapshot Current { get; private set; } = new(true, false, false, "", "", Array.Empty<PlayerSnapshot>(), "");
    public IReadOnlyList<object> Observe(bool server, bool client, string scene, IEnumerable<PlayerSnapshot> players)
    {
        var events = new List<object>();
        var previous = Current;
        var nextPlayers = server ? players.OrderBy(p => p.ConnectionId).ToArray() : Array.Empty<PlayerSnapshot>();
        var session = server ? (previous.IsServer ? previous.SessionId : Guid.NewGuid().ToString("N")) : "";
        if (!previous.IsServer && server) events.Add(new ServerStarted(session));
        foreach (var player in previous.Players)
            if (!nextPlayers.Any(p => p.ConnectionId == player.ConnectionId && p.NetId == player.NetId))
                events.Add(new PlayerUnavailable(previous.SessionId, player));
        foreach (var player in nextPlayers)
            if (!previous.Players.Any(p => p.ConnectionId == player.ConnectionId && p.NetId == player.NetId))
                events.Add(new PlayerAvailable(session, player));
        if (previous.IsServer && !server) events.Add(new ServerStopped(previous.SessionId));
        Current = new(true, server, client, session, scene, Array.AsReadOnly(nextPlayers), "");
        return events;
    }
}
