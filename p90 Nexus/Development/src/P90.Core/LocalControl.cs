using System.Diagnostics;
using System.Text.Json;
using P90.API;
namespace P90.Core;

// Local filesystem mailbox. No TCP listener, game chat handler or remote player endpoint.
public sealed class LocalControl : IDisposable
{
    public sealed record Request(string Id, string Session, DateTime CreatedUtc, string Command, string[] Arguments);
    public sealed record Reply(string Id, bool Success, string Message);
    public string Session { get; } = Guid.NewGuid().ToString("N");
    private readonly PluginHost host;
    private readonly string folder;
    private readonly HashSet<string> consumed = new(StringComparer.Ordinal);
    private DateTime nextHeartbeat;
    private bool disposed;
    public LocalControl(PluginHost host)
    {
        this.host = host; folder = Path.Combine(host.Root, "control");
        Directory.CreateDirectory(Path.Combine(folder, "inbox"));
        Directory.CreateDirectory(Path.Combine(folder, "replies"));
        Heartbeat(true);
    }
    private static void AtomicWrite(string path, object value)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value));
        File.Move(temporary, path, true);
    }
    private void Heartbeat(bool ready)
    {
        AtomicWrite(Path.Combine(folder, "session.json"), new
        {
            Session, ProcessId = Environment.ProcessId, ProcessStartedUtc = Process.GetCurrentProcess().StartTime.ToUniversalTime(),
            UpdatedUtc = DateTime.UtcNow, Ready = ready
        });
        nextHeartbeat = DateTime.UtcNow.AddSeconds(1);
    }
    public void Poll()
    {
        host.CheckThread();
        if (disposed) return;
        if (DateTime.UtcNow >= nextHeartbeat) Heartbeat(true);
        foreach (var path in Directory.EnumerateFiles(Path.Combine(folder, "inbox"), "*.json").Take(4))
        {
            var id = Path.GetFileNameWithoutExtension(path);
            // No request-supplied value is ever used as a filesystem path.
            if (!Guid.TryParseExact(id, "N", out _)) continue;
            var claim = path + ".processing";
            try { File.Move(path, claim); } catch (IOException) { continue; }
            Reply reply;
            try
            {
                if (new FileInfo(claim).Length > 16384) throw new InvalidDataException("Request exceeds 16 KiB.");
                var request = JsonSerializer.Deserialize<Request>(File.ReadAllText(claim)) ?? throw new InvalidDataException("Empty request.");
                reply = ExecuteRequest(id, request);
            }
            catch (Exception error) { reply = new(id, false, "Request rejected: " + error.Message); }
            AtomicWrite(Path.Combine(folder, "replies", id + ".json"), reply);
            File.Delete(claim);
        }
    }
    public Reply ExecuteRequest(string filenameId, Request request)
    {
        host.CheckThread();
        if (disposed) return new(filenameId, false, "Control has stopped.");
        var age = DateTime.UtcNow - request.CreatedUtc;
        if (request.Id != filenameId || !Guid.TryParseExact(filenameId, "N", out _) || request.Session != Session || request.CreatedUtc.Kind != DateTimeKind.Utc || age.TotalSeconds is < -5 or > 30)
            return new(filenameId, false, "Wrong ID/session or expired request. Send a new command.");
        if (consumed.Count >= 10000 || !consumed.Add(filenameId)) return new(filenameId, false, "Duplicate request or session command limit reached.");
        if (string.IsNullOrWhiteSpace(request.Command) || request.Arguments is null || request.Arguments.Any(x => x is null) || request.Arguments.Length > 64)
            return new(filenameId, false, "Invalid command arguments.");
        var result = Dispatch(request.Command, request.Arguments);
        host.Write("core", result.Success ? "Info" : "Error", $"Local command {request.Command}: {result.Message}");
        return new(filenameId, result.Success, result.Message);
    }
    private CommandResult Dispatch(string command, string[] args)
    {
        if (command == "p90.help") return CommandResult.Ok("p90.plugins | p90.commands | p90.stop ID | p90.start ID | p90.restart ID; server.status | server.players | server.announce TEXT | server.clear");
        if (command == "p90.plugins") return CommandResult.Ok(JsonSerializer.Serialize(host.Plugins.Select(p => new { p.Id, p.Version, State = p.State.ToString() })));
        if (command == "p90.commands") return CommandResult.Ok(string.Join(Environment.NewLine, host.CommandNames));
        if (command == "p90.commandinfo") return CommandResult.Ok(JsonSerializer.Serialize(host.CommandCatalog));
        if (command is "p90.stop" or "p90.start" or "p90.restart")
        {
            if (args.Length != 1) return CommandResult.Fail("Provide exactly one plugin ID.");
            HostResult result;
            if (command == "p90.stop") result = host.Stop(args[0]);
            else if (command == "p90.start") result = host.Start(args[0]);
            else
            {
                result = host.Stop(args[0]);
                if (result.Success) result = host.Start(args[0]);
            }
            return new(result.Success, result.Message);
        }
        return host.ExecuteLocal(command, args);
    }
    public void Dispose() { host.CheckThread(); if (disposed) return; disposed = true; Heartbeat(false); }
}
