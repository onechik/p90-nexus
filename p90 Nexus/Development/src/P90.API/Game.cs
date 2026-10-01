namespace P90.API;

// Snapshots contain no Unity/IL2CPP objects. IDs are scoped to a server session.
public sealed record PlayerSnapshot(int ConnectionId, uint NetId, string Name);
public sealed record ServerSnapshot(bool Supported, bool IsServer, bool IsClient, string SessionId,
    string Scene, IReadOnlyList<PlayerSnapshot> Players, string DisabledReason);
public interface IGameServer
{
    ServerSnapshot Snapshot { get; }
    // Success means queued through the game's existing RPC, not delivery acknowledgement.
    CommandResult Broadcast(string text);
}
public sealed record ServerStarted(string SessionId);
public sealed record ServerStopped(string SessionId);
// Observed ready player identities, not low-level transport connect/disconnect callbacks.
public sealed record PlayerAvailable(string SessionId, PlayerSnapshot Player);
public sealed record PlayerUnavailable(string SessionId, PlayerSnapshot Player);
