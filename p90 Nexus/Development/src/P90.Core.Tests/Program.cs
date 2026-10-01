using System.Text.Json;
using P90.API;
using P90.Core;
using P90.Samples.Lifecycle;
using P90.Samples.Diagnostics;

var root = Path.GetFullPath(args.Single());
Directory.CreateDirectory(root);
var results = new List<object>();
int failed = 0;
void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}
void Test(string name, Action<PluginHost> run)
{
    var host = new PluginHost(Path.Combine(root, name));
    try { run(host); results.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); }
    catch (Exception error) { failed++; results.Add(new { name, passed = false, error = error.ToString() }); Console.WriteLine("FAIL " + name + ": " + error); }
    finally { host.StopAll(); }
}

Test("discovery-and-restart", h =>
{
    foreach (var type in new[] { typeof(LifecyclePlugin), typeof(DiagnosticsPlugin) })
        File.Copy(type.Assembly.Location, Path.Combine(h.Root, "plugins", Path.GetFileName(type.Assembly.Location)));
    Assert(h.DiscoverAndLoad() is { Count: 2 } loaded && loaded.All(r => r.Success), "Discover two independent DLLs");
    Assert(h.CommandCount == 5 && h.SubscriptionCount == 2, "Expected registrations");
    Assert(h.DiscoverAndLoad().All(r => !r.Success), "Repeated discovery rejects duplicates");
    Assert(!h.Start("sample.lifecycle").Success && h.CommandCount == 5, "Active restart rejected");
    h.Tick(); Assert(h.ExecuteLocal("SAMPLE.PULSES").Message == "1", "Pulse and case insensitive lookup");
    h.Stop("sample.lifecycle"); Assert(!h.ExecuteLocal("sample.hello").Success && h.SubscriptionCount == 1, "Stopped handlers unavailable");
    for (int i = 0; i < 25; i++) { Assert(h.Start("sample.lifecycle").Success, "Restart"); h.Stop("sample.lifecycle"); }
    Assert(h.CommandCount == 3 && h.SubscriptionCount == 1, "No registrations accumulate");
    h.StopAll(); h.StopAll(); Assert(h.CommandCount == 0 && h.SubscriptionCount == 0, "Idempotent shutdown");
});
Test("plugin-folders-and-legacy-upgrade", h =>
{
    var folder = Path.Combine(h.Root, "plugins", "Lifecycle"); Directory.CreateDirectory(folder);
    var assembly = typeof(LifecyclePlugin).Assembly.Location;
    File.Copy(assembly, Path.Combine(folder, Path.GetFileName(assembly)));
    File.Copy(assembly, Path.Combine(h.Root, "plugins", Path.GetFileName(assembly)));
    var other = typeof(DiagnosticsPlugin).Assembly.Location;
    File.Copy(other, Path.Combine(h.Root, "plugins", Path.GetFileName(other)));
    var ignored = Path.Combine(folder, "Sources"); Directory.CreateDirectory(ignored);
    File.WriteAllText(Path.Combine(ignored, "Unused.dll"), "not a runtime DLL");
    var loaded = h.DiscoverAndLoad();
    Assert(loaded.Count == 2 && loaded.All(r => r.Success), "Folder overrides old loose copy; legacy plugin remains supported");
    Assert(h.ExecuteLocal("sample.hello").Success && h.CommandCount == 5, "Folder plugin and old plugin run once");
    Assert(File.ReadAllText(Path.Combine(h.Root,"logs","core.jsonl")).Contains("legacy"), "Migration reported");
});
Test("folder-bundled-dependency", h =>
{
    var folder = Path.Combine(h.Root, "plugins", "DependencyPlugin"); Directory.CreateDirectory(folder);
    var fixture = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../P90.DiscoveryFixture/bin/Release/net6.0"));
    foreach(var name in new[]{"A.P90.DiscoveryPlugin.dll","Z.P90.DiscoverySupport.dll"})File.Copy(Path.Combine(fixture,name),Path.Combine(folder,name));
    var loaded = h.DiscoverAndLoad();
    Assert(loaded.Count == 1 && loaded[0].Success, "Dependency is not mistaken for a plugin");
    Assert(h.ExecuteLocal("folder.dependency").Message == "dependency-loaded", "Lazy command resolves bundled library");
});
Test("folder-discovery-error-isolation", h =>
{
    var folder = Path.Combine(h.Root, "plugins", "Working"); Directory.CreateDirectory(folder);
    File.Copy(typeof(LifecyclePlugin).Assembly.Location, Path.Combine(folder, "P90.Samples.Lifecycle.dll"));
    var broken = Path.Combine(h.Root, "plugins", "Broken"); Directory.CreateDirectory(broken);
    File.WriteAllText(Path.Combine(broken, "Broken.dll"), "bad dll");
    var loaded = h.DiscoverAndLoad();
    Assert(loaded.Any(r => !r.Success) && h.ExecuteLocal("sample.hello").Success, "Bad folder does not prevent good plugin");
});
Test("command-metadata", h =>
{
    h.LoadType(typeof(ChurnPlugin));
    var commands = ChurnPlugin.Context!.Commands;
    using var token = commands.Register("unrelated.command", _ => CommandResult.Ok("ok"), "Описание\nВторая строка", "unrelated.command TEXT");
    var item = h.CommandCatalog.Single();
    Assert(item.PluginId == "test.churn" && item.Description.Contains("Вторая") && item.Usage == "unrelated.command TEXT", "Metadata and real owner");
    Throws<InvalidOperationException>(() => commands.Register("unrelated.command", _ => CommandResult.Ok("bad"), "bad", "bad"));
    Assert(h.CommandCatalog.Single() == item, "Conflict preserves metadata");
    Throws<ArgumentException>(() => commands.Register("invalid.meta", _ => CommandResult.Ok("bad"), "\u001b[31m", "bad"));
    using var oldStyle = commands.Register("old.style", _ => CommandResult.Ok("old"));
    Assert(h.CommandCatalog.Single(x => x.Name == "old.style").Description == "", "Old overload retains owner without invented description");
    using var control = new LocalControl(h);
    var id = Guid.NewGuid().ToString("N");
    var reply = control.ExecuteRequest(id, new LocalControl.Request(id, control.Session, DateTime.UtcNow, "p90.commandinfo", Array.Empty<string>()));
    Assert(reply.Success && JsonSerializer.Deserialize<CommandInfo[]>(reply.Message)!.Length == 2, "Metadata endpoint JSON");
    token.Dispose(); Assert(h.CommandCatalog.All(x => x.Name != "unrelated.command"), "Dispose removes metadata");
    h.StopAll(); Assert(h.CommandCatalog.Count == 0, "Stop clears metadata");
    Throws<ObjectDisposedException>(() => commands.Register("stale.meta", _ => CommandResult.Ok("bad"), "description", "usage"));
});
Test("handler-error-isolation", h =>
{
    h.LoadType(typeof(DiagnosticsPlugin)); h.LoadType(typeof(LifecyclePlugin));
    h.ExecuteLocal("diagnostic.arm"); h.Tick();
    Assert(h.HandlerErrors == 1 && h.ExecuteLocal("sample.pulses").Message == "1", "Throwing first subscriber doesn't block second");
    Assert(!h.ExecuteLocal("diagnostic.fail").Success && h.HandlerErrors == 2, "Command exception contained");
    h.Tick(); Assert(h.ExecuteLocal("sample.pulses").Message == "2" && h.ExecuteLocal("sample.hello").Success, "Continues after errors");
    Assert(File.ReadAllText(Path.Combine(h.Root, "logs", "sample.diagnostics.jsonl")).Contains("Intentional event fault"), "Detailed own log");
});
Test("metadata-before-construction", h =>
{
    Incompatible.Constructions = 0;
    Assert(!h.LoadType(typeof(Incompatible)).Success && Incompatible.Constructions == 0, "Unsupported API rejected before constructor");
    Assert(!h.LoadType(typeof(BadId)).Success && !h.LoadType(typeof(AbstractPlugin)).Success, "Reject unsafe ID / abstract class");
    h.LoadType(typeof(LifecyclePlugin));
    Assert(!h.LoadType(typeof(Duplicate)).Success && h.CommandCount == 2, "Duplicate ID does not disturb original");
});
Test("failed-start-rollback", h =>
{
    Assert(!h.LoadType(typeof(FailStart)).Success, "Start rejected");
    Assert(h.CommandCount == 0 && h.SubscriptionCount == 0 && h.Plugins.Single().State == PluginState.Faulted, "Partial registrations rolled back even if Stop throws");
    Throws<ObjectDisposedException>(() => FailStart.Context!.Commands.Register("stale.command", _ => CommandResult.Ok("bad")));
});
Test("command-conflict-preserves-owner", h =>
{
    h.LoadType(typeof(LifecyclePlugin));
    Assert(!h.LoadType(typeof(Conflict)).Success, "Conflicting start rejected");
    Assert(h.CommandCount == 2 && h.SubscriptionCount == 1 && h.ExecuteLocal("sample.hello").Success, "Only failed plugin cleaned up");
});
Test("stop-error-and-stale-context", h =>
{
    h.LoadType(typeof(FailStop)); var old = FailStop.Context!;
    Assert(!h.Stop("test.stop").Success && h.CommandCount == 0 && h.SubscriptionCount == 0, "Stop failure still releases registrations");
    Assert(h.Start("test.stop").Success, "Restart allowed after stop error");
    Throws<ObjectDisposedException>(() => old.Events.Subscribe<int>(_ => { }));
    Assert(h.CommandCount == 1 && h.SubscriptionCount == 1, "Fresh context only");
});
Test("config-roundtrip-and-malformed", h =>
{
    h.LoadType(typeof(LifecyclePlugin)); h.StopAll();
    var path = Path.Combine(h.Root, "config", "sample.lifecycle.json");
    File.WriteAllText(path, "{\"Greeting\":\"Федор\"}");
    Assert(h.Start("sample.lifecycle").Success && h.ExecuteLocal("sample.hello").Message == "Федор", "Reads edited config on restart");
    h.StopAll(); File.WriteAllText(path, "{broken");
    Assert(!h.Start("sample.lifecycle").Success && File.ReadAllText(path) == "{broken", "Malformed config preserved");
    Assert(h.CommandCount == 0 && h.SubscriptionCount == 0, "Malformed config doesn't leave active plugin");
});
Test("thread-and-disposal", h =>
{
    h.LoadType(typeof(DisposableHandlers));
    Task.Run(() => Throws<InvalidOperationException>(() => h.Tick())).GetAwaiter().GetResult();
    Task.Run(() => Throws<InvalidOperationException>(() => DisposableHandlers.Context!.Config.Load<LifecyclePlugin.Settings>())).GetAwaiter().GetResult();
    h.Tick(); h.Tick();
    Assert(DisposableHandlers.First == 2 && DisposableHandlers.Second == 0 && DisposableHandlers.LastPulse == 2, "Removed snapshot callback skipped; foreign thread cannot advance pulse");
    h.StopAll(); Assert(h.SubscriptionCount == 0, "Disposed token remains safe on final cleanup");
});
Test("server-observation-lifecycle", h =>
{
    var tracker = new ServerTracker();
    var player = new PlayerSnapshot(0, 17, "Host");
    Assert(tracker.Observe(false, true, "Sandbox", new[] { player }).Count == 0 && tracker.Current.Players.Count == 0, "Client cannot expose server players");
    var start = tracker.Observe(true, true, "Sandbox", new[] { player });
    var session = tracker.Current.SessionId;
    Assert(start.Count == 2 && start[0] is ServerStarted && start[1] is PlayerAvailable, "Host emits one start and one ready event");
    Assert(tracker.Observe(true, true, "Sandbox", new[] { player }).Count == 0, "No duplicate host/client events");
    var replacement = tracker.Observe(true, true, "Facility", new[] { player with { NetId = 18 } });
    Assert(replacement.Count == 2 && replacement[0] is PlayerUnavailable && replacement[1] is PlayerAvailable, "Respawned identity replaces prior snapshot");
    var stop = tracker.Observe(false, false, "MainMenu", Array.Empty<PlayerSnapshot>());
    Assert(stop.Count == 2 && stop[0] is PlayerUnavailable && stop[1] is ServerStopped && tracker.Current.SessionId == "", "Stop clears player/session state");
    tracker.Observe(true, true, "Sandbox", new[] { player });
    Assert(tracker.Current.SessionId != session, "Restart cannot reuse session identity");
});
Test("server-service-scope", h =>
{
    h.LoadType(typeof(FailStop)); var service = FailStop.Context!.Server;
    Assert(!service.Snapshot.Supported && !service.Broadcast("x").Success, "Missing adapter fails closed");
    h.StopAll(); Throws<ObjectDisposedException>(() => service.Broadcast("x"));
});
Test("local-control-session-and-replay", h =>
{
    h.LoadType(typeof(LifecyclePlugin));
    using var control = new LocalControl(h);
    var id = Guid.NewGuid().ToString("N");
    var request = new LocalControl.Request(id, control.Session, DateTime.UtcNow, "p90.stop", new[] { "sample.lifecycle" });
    Assert(!control.ExecuteRequest(id, request with { Session = "old" }).Success && h.CommandCount == 2, "Stale session cannot act");
    Assert(!control.ExecuteRequest(id, request with { CreatedUtc = DateTime.UtcNow.AddMinutes(-1) }).Success, "Expired request denied");
    Assert(control.ExecuteRequest(id, request).Success && h.CommandCount == 0, "Authorized local stop");
    h.Start("sample.lifecycle");
    Assert(!control.ExecuteRequest(id, request).Success && h.CommandCount == 2, "Replay cannot stop restarted plugin");
    var nextId = Guid.NewGuid().ToString("N");
    var inbox = Path.Combine(h.Root, "control", "inbox", nextId + ".json");
    File.WriteAllText(inbox, JsonSerializer.Serialize(request with { Id = nextId, Command = "sample.hello", Arguments = Array.Empty<string>() }));
    control.Poll();
    var reply = JsonSerializer.Deserialize<LocalControl.Reply>(File.ReadAllText(Path.Combine(h.Root, "control", "replies", nextId + ".json")))!;
    Assert(reply.Success && !File.Exists(inbox), "Mailbox consumes request and returns result");
    control.Dispose();
    Assert(!control.ExecuteRequest(Guid.NewGuid().ToString("N"), request).Success, "Stopped control rejects commands");
});
Test("registration-churn", h =>
{
    h.LoadType(typeof(ChurnPlugin));
    for (int i = 0; i < 10000; i++)
    {
        using var command = ChurnPlugin.Context!.Commands.Register("churn.temporary", _ => CommandResult.Ok("ok"));
        using var subscription = ChurnPlugin.Context.Events.Subscribe<int>(_ => { });
    }
    Assert(h.CommandCount == 0 && h.SubscriptionCount == 0 && h.TrackedRegistrationCount == 0, "Early disposal must release tracking tokens while plugin stays active");
    for (int i = 0; i < 100; i++)
    {
        ChurnPlugin.Context!.Commands.Register("churn.owned", _ => CommandResult.Ok("ok"));
        ChurnPlugin.Context.Events.Subscribe<int>(_ => { });
        h.StopAll();
        Assert(h.TrackedRegistrationCount == 0, "Stop releases every tracked registration");
        h.Start("test.churn");
    }
});
Test("invalid-dll-and-file-identifiers", h =>
{
    File.WriteAllText(Path.Combine(h.Root, "plugins", "000-broken.dll"), "not an assembly");
    File.Copy(typeof(LifecyclePlugin).Assembly.Location, Path.Combine(h.Root, "plugins", "valid.dll"));
    var loaded = h.DiscoverAndLoad();
    Assert(loaded.Count == 2 && !loaded[0].Success && loaded[1].Success, "Broken DLL must not prevent next plugin load");
    Assert(!h.LoadType(typeof(NewlineId)).Success && !h.LoadType(typeof(DeviceId)).Success, "Reject newline and Windows reserved device IDs");
});
Test("build-guard-fails-closed", h =>
{
    Assert(P90.GameAdapter.BuildGuard.Verify(h.Root).StartsWith("Cannot verify"), "Missing game cannot be accepted");
    File.WriteAllText(Path.Combine(h.Root, "GameAssembly.dll"), "unsupported build fixture");
    Assert(P90.GameAdapter.BuildGuard.Verify(h.Root).StartsWith("Unsupported game build"), "Changed binary cannot be accepted");
});
Test("repeated-server-sessions", h =>
{
    var tracker = new ServerTracker();
    var ids = new HashSet<string>();
    for (int i = 0; i < 1000; i++)
    {
        var players = new[] { new PlayerSnapshot(0, 3, "host"), new PlayerSnapshot(1, 4, "guest") };
        Assert(tracker.Observe(true, true, "Sandbox", players).Count == 3, "One start and two available events");
        Assert(ids.Add(tracker.Current.SessionId), "Fresh session ID");
        Assert(tracker.Observe(true, true, "Sandbox", players).Count == 0, "Stable frame has no duplicates");
        Assert(tracker.Observe(false, false, "MainMenu", players).Count == 3 && tracker.Current.Players.Count == 0, "Stop removes all session state");
    }
});
File.WriteAllText(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new { passed = failed == 0, count = results.Count, failed, results }, new JsonSerializerOptions { WriteIndented = true }));
return failed == 0 ? 0 : 1;

[Plugin("test.future", "Future", "1.0", 2)]
public class Incompatible : IP90Plugin { public static int Constructions; public Incompatible() => Constructions++; public void Start(IPluginContext c) { } public void Stop() { } }
[Plugin("../escape", "Bad", "1.0")]
public class BadId : Incompatible { }
[Plugin("sample.lifecycle", "Duplicate", "1.0")]
public class Duplicate : Incompatible { }
[Plugin("test.abstract", "Abstract", "1.0")]
public abstract class AbstractPlugin : Incompatible { }
[Plugin("test.start", "Fail start", "1.0")]
public class FailStart : IP90Plugin
{
    public static IPluginContext? Context;
    public void Start(IPluginContext c) { Context = c; c.Commands.Register("partial.command", _ => CommandResult.Ok("x")); c.Events.Subscribe<int>(_ => { }); throw new Exception("start failure"); }
    public void Stop() => throw new Exception("cleanup failure");
}
[Plugin("test.conflict", "Conflict", "1.0")]
public class Conflict : IP90Plugin
{
    public void Start(IPluginContext c) { c.Events.Subscribe<int>(_ => { }); c.Commands.Register("sample.hello", _ => CommandResult.Ok("wrong")); }
    public void Stop() { }
}
[Plugin("test.stop", "Fail stop", "1.0")]
public class FailStop : IP90Plugin
{
    public static IPluginContext? Context;
    public void Start(IPluginContext c) { Context = c; c.Commands.Register("stop.command", _ => CommandResult.Ok("x")); c.Events.Subscribe<int>(_ => { }); }
    public void Stop() => throw new Exception("stop failure");
}
[Plugin("test.dispose", "Disposable handlers", "1.0")]
public class DisposableHandlers : IP90Plugin
{
    public static IPluginContext? Context;
    public static int First, Second;
    public static long LastPulse;
    public void Start(IPluginContext c)
    {
        Context = c; First = Second = 0; LastPulse = 0; IDisposable? second = null;
        c.Events.Subscribe<CorePulse>(p => { First++; LastPulse = p.Number; second!.Dispose(); });
        second = c.Events.Subscribe<CorePulse>(_ => Second++);
    }
    public void Stop() { }
}

[Plugin("test.churn", "Registration churn", "1.0")]
public class ChurnPlugin : IP90Plugin
{
    public static IPluginContext? Context;
    public void Start(IPluginContext context) => Context = context;
    public void Stop() { }
}
[Plugin("bad.id\n", "Newline", "1.0")]
public class NewlineId : Incompatible { }
[Plugin("con.example", "Windows device", "1.0")]
public class DeviceId : Incompatible { }
