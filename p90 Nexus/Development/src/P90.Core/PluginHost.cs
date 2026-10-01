using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.RegularExpressions;
using P90.API;

namespace P90.Core;

public enum PluginState { Starting, Active, Stopping, Stopped, Faulted }
public sealed record HostResult(bool Success, string Message);
public sealed record PluginSnapshot(string Id, string Version, PluginState State);
public sealed record LogEntry(DateTime Utc, string PluginId, string Level, string Message);

public sealed class PluginHost
{
    public const int ApiMajor = 1;
    private readonly int threadId = Environment.CurrentManagedThreadId;
    private readonly Dictionary<string, Entry> plugins = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Type, List<Subscription>> subscriptions = new();
    private readonly Dictionary<string, Command> commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> loadOrder = new();
    private readonly Action<LogEntry>? sink;
    private readonly IGameServer server;
    private long pulse;
    public string Root { get; }
    public int HandlerErrors { get; private set; }
    public int CommandCount { get { CheckThread(); return commands.Count; } }
    public int SubscriptionCount { get { CheckThread(); return subscriptions.Values.Sum(x => x.Count); } }
    public int TrackedRegistrationCount { get { CheckThread(); return plugins.Values.Sum(x => x.Scope?.RegistrationCount ?? 0); } }
    public IReadOnlyList<string> CommandNames { get { CheckThread(); return commands.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray(); } }
    public IReadOnlyList<CommandInfo> CommandCatalog
    {
        get { CheckThread(); return commands.Where(p => p.Value.Owner.CanDispatch)
            .OrderBy(p => p.Value.Owner.PluginId, StringComparer.Ordinal).ThenBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => new CommandInfo(p.Key, p.Value.Owner.PluginId, p.Value.Description, p.Value.Usage)).ToArray(); }
    }
    public IReadOnlyList<PluginSnapshot> Plugins
    {
        get { CheckThread(); return loadOrder.Select(id => plugins[id]).Select(e => new PluginSnapshot(e.Metadata.Id, e.Metadata.Version, e.State)).ToArray(); }
    }

    public PluginHost(string root, Action<LogEntry>? logSink = null, IGameServer? gameServer = null)
    {
        Root = Path.GetFullPath(root);
        sink = logSink;
        server = gameServer ?? new UnavailableGameServer();
        foreach (var folder in new[] { "plugins", "config", "logs" }) Directory.CreateDirectory(Path.Combine(Root, folder));
    }

    public IReadOnlyList<HostResult> DiscoverAndLoad()
    {
        CheckThread();
        var results = new List<HostResult>();
        var pluginRoot = Path.Combine(Root, "plugins");
        // A distributable plugin lives directly inside its own folder. Keep legacy loose DLLs.
        // Preload managed dependencies before inspecting types; one shared runtime/API remains.
        var paths = Directory.GetDirectories(pluginRoot).OrderBy(p => p, StringComparer.Ordinal)
            .Where(p => !Path.GetFileName(p).StartsWith('.') && (File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0)
            .SelectMany(p => Directory.GetFiles(p, "*.dll").OrderBy(f => f, StringComparer.Ordinal))
            .Concat(Directory.GetFiles(pluginRoot, "*.dll").OrderBy(p => p, StringComparer.Ordinal));
        var selected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var assemblies = new List<(string Path, Assembly Assembly)>();
        foreach (var path in paths)
        {
            try
            {
                var name = AssemblyName.GetAssemblyName(path).Name!;
                if (selected.TryGetValue(name, out var previous))
                {
                    if (Path.GetDirectoryName(path) == pluginRoot && Path.GetDirectoryName(previous) != pluginRoot)
                    {
                        Write("core", "Info", $"Using plugin folder {Path.GetRelativePath(pluginRoot, previous)}; legacy {Path.GetFileName(path)} skipped.");
                        continue;
                    }
                    Write("core", "Error", $"Duplicate assembly {name}: {previous} and {path}. Keep one copy.");
                    results.Add(new(false, $"Duplicate assembly: {name}"));
                    continue;
                }
                selected.Add(name, path);
                var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path));
                assemblies.Add((path, assembly));
            }
            catch (Exception error)
            {
                Write("core", "Error", $"Cannot inspect {Path.GetFileName(path)}: {error}");
                results.Add(new(false, $"Cannot inspect {Path.GetFileName(path)}"));
            }
        }
        foreach (var (path, assembly) in assemblies)
        {
            try
            {
                foreach (var type in assembly.GetTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
                    if (type.GetCustomAttribute<PluginAttribute>() is not null) results.Add(LoadType(type));
            }
            catch (Exception error)
            {
                Write("core", "Error", $"Cannot inspect {Path.GetRelativePath(pluginRoot, path)}: {error}");
                results.Add(new(false, $"Cannot inspect {Path.GetFileName(path)}"));
            }
        }
        return results;
    }

    public HostResult LoadType(Type type)
    {
        CheckThread();
        PluginAttribute? metadata;
        try { metadata = type.GetCustomAttribute<PluginAttribute>(); }
        catch (Exception error) { return Reject("Metadata read failed: " + error.Message); }
        if (metadata is null) return Reject("Missing PluginAttribute.");
        if (!ValidId(metadata.Id) || metadata.Id.Equals("core", StringComparison.OrdinalIgnoreCase)) return Reject("Invalid or reserved plugin ID.");
        if (string.IsNullOrWhiteSpace(metadata.Name) || !Version.TryParse(metadata.Version, out _)) return Reject("Invalid plugin name/version.");
        if (metadata.ApiMajor != ApiMajor) return Reject($"{metadata.Id}: API {metadata.ApiMajor} is unsupported (host: {ApiMajor}).");
        if (plugins.ContainsKey(metadata.Id)) return Reject($"Duplicate plugin ID: {metadata.Id}");
        if (!type.IsClass || type.IsAbstract || type.ContainsGenericParameters || !typeof(IP90Plugin).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) is null)
            return Reject($"{metadata.Id}: requires a concrete IP90Plugin with a public parameterless constructor.");
        try
        {
            var instance = (IP90Plugin)Activator.CreateInstance(type)!;
            var entry = new Entry(metadata, instance);
            plugins.Add(metadata.Id, entry);
            loadOrder.Add(metadata.Id);
            return Start(metadata.Id);
        }
        catch (Exception error)
        {
            Write(metadata.Id, "Error", "Construction failed: " + error);
            return new(false, $"{metadata.Id}: construction failed.");
        }
    }

    public HostResult Start(string id)
    {
        CheckThread();
        if (!plugins.TryGetValue(id, out var entry)) return new(false, $"Unknown plugin: {id}");
        if (entry.State is PluginState.Active or PluginState.Starting or PluginState.Stopping) return new(false, $"{id}: invalid start state {entry.State}");
        entry.State = PluginState.Starting;
        entry.Scope = new Scope(this, entry.Metadata.Id);
        try
        {
            entry.Plugin.Start(entry.Scope);
            entry.State = PluginState.Active;
            Write(entry.Metadata.Id, "Info", "Plugin started.");
            return new(true, $"{id}: started.");
        }
        catch (Exception error)
        {
            Write(entry.Metadata.Id, "Error", "Start failed: " + error);
            try { entry.Plugin.Stop(); }
            catch (Exception cleanup) { Write(entry.Metadata.Id, "Error", "Failed-start cleanup: " + cleanup); }
            finally { entry.Scope.Dispose(); entry.State = PluginState.Faulted; }
            return new(false, $"{id}: start failed; registrations rolled back.");
        }
    }

    public HostResult Stop(string id)
    {
        CheckThread();
        if (!plugins.TryGetValue(id, out var entry)) return new(false, $"Unknown plugin: {id}");
        if (entry.State is PluginState.Stopped or PluginState.Faulted) return new(true, $"{id}: already inactive.");
        if (entry.State != PluginState.Active) return new(false, $"{id}: invalid stop state {entry.State}");
        entry.State = PluginState.Stopping;
        bool success = true;
        try { entry.Plugin.Stop(); }
        catch (Exception error) { success = false; Write(id, "Error", "Stop failed: " + error); }
        finally { entry.Scope!.Dispose(); entry.State = PluginState.Stopped; }
        Write(id, "Info", "Plugin stopped; registrations released.");
        return new(success, $"{id}: stopped" + (success ? "." : " with an error; registrations still released."));
    }

    public void StopAll()
    {
        CheckThread();
        foreach (var id in loadOrder.AsEnumerable().Reverse().ToArray()) Stop(id);
    }

    public void Tick() { CheckThread(); Publish(new CorePulse(++pulse)); }

    public void Publish<T>(T value)
    {
        CheckThread();
        if (!subscriptions.TryGetValue(typeof(T), out var listeners)) return;
        foreach (var item in listeners.ToArray())
        {
            if (!item.Enabled || !item.Owner.CanDispatch) continue;
            try { ((Action<T>)item.Callback)(value); }
            catch (Exception error) { HandlerErrors++; Write(item.Owner.PluginId, "Error", $"Event {typeof(T).Name}: {error}"); }
        }
    }

    // Local host-only dispatch. No player/chat/network endpoint exists at this stage.
    public CommandResult ExecuteLocal(string name, params string[] arguments)
    {
        CheckThread();
        if (!commands.TryGetValue(name, out var command) || !command.Owner.CanDispatch) return CommandResult.Fail("Command not available: " + name);
        try { return command.Callback(new CommandContext(Array.AsReadOnly((string[])arguments.Clone()))) ?? CommandResult.Fail("Command returned no result."); }
        catch (Exception error)
        {
            HandlerErrors++;
            Write(command.Owner.PluginId, "Error", $"Command {name}: {error}");
            return CommandResult.Fail("Command handler failed; see plugin log.");
        }
    }

    internal void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != threadId) throw new InvalidOperationException("P90 operations must run on the host thread.");
    }

    private static bool ValidId(string? id) => id is not null
        && Regex.IsMatch(id, "\\A[a-z][a-z0-9.-]{2,63}\\z", RegexOptions.CultureInvariant)
        && !Regex.IsMatch(id, "\\A(?:con|prn|aux|nul|com[0-9]|lpt[0-9])(?:\\.|\\z)", RegexOptions.CultureInvariant);
    private HostResult Reject(string reason) { Write("core", "Error", reason); return new(false, reason); }
    internal void Write(string id, string level, string message)
    {
        var record = new LogEntry(DateTime.UtcNow, id, level, message);
        try { File.AppendAllText(Path.Combine(Root, "logs", id + ".jsonl"), JsonSerializer.Serialize(record) + Environment.NewLine); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            try { sink?.Invoke(record with { Level = "Error", Message = "File log failed: " + error.Message + "; original: " + message }); } catch { }
        }
        try { sink?.Invoke(record); } catch { /* A diagnostic sink must not break plugin lifecycle. */ }
    }

    private sealed class Entry
    {
        public Entry(PluginAttribute metadata, IP90Plugin plugin) => (Metadata, Plugin) = (metadata, plugin);
        public PluginAttribute Metadata { get; }
        public IP90Plugin Plugin { get; }
        public PluginState State { get; set; } = PluginState.Stopped;
        public Scope? Scope { get; set; }
    }
    private sealed record Command(Scope Owner, Func<CommandContext, CommandResult> Callback, string Description, string Usage);
    private sealed class Subscription
    {
        public Subscription(Scope owner, Delegate callback) => (Owner, Callback) = (owner, callback);
        public Scope Owner { get; }
        public Delegate Callback { get; }
        public bool Enabled { get; set; } = true;
    }

    private sealed class Token : IDisposable
    {
        private readonly PluginHost host;
        private Action? remove;
        public Token(PluginHost host, Action remove) => (this.host, this.remove) = (host, remove);
        public void Dispose() { host.CheckThread(); var action = remove; remove = null; action?.Invoke(); }
    }

    private sealed class Scope : IPluginContext, IPluginLog, IPluginConfig, IPluginEvents, IDescribedPluginCommands, IGameServer, IDisposable
    {
        private readonly PluginHost host;
        private readonly HashSet<IDisposable> owned = new();
        public int RegistrationCount => owned.Count;
        private bool alive = true;
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
        public Scope(PluginHost host, string id) => (this.host, PluginId) = (host, id);
        public string PluginId { get; }
        public IPluginLog Log => this;
        public IPluginConfig Config => this;
        public IPluginEvents Events => this;
        public IPluginCommands Commands => this;
        public IGameServer Server => this;
        public ServerSnapshot Snapshot { get { Guard(); return host.server.Snapshot; } }
        public CommandResult Broadcast(string text) { Guard(); return host.server.Broadcast(text); }
        public bool CanDispatch => alive && host.plugins[PluginId].State == PluginState.Active;
        private void Guard() { host.CheckThread(); if (!alive) throw new ObjectDisposedException(PluginId, "Plugin context has stopped."); }
        public void Info(string message) { Guard(); host.Write(PluginId, "Info", message); }
        public void Error(string message) { Guard(); host.Write(PluginId, "Error", message); }
        private string ConfigPath => Path.Combine(host.Root, "config", PluginId + ".json");
        public T Load<T>() where T : class, new()
        {
            Guard();
            if (!File.Exists(ConfigPath)) { var value = new T(); Save(value); return value; }
            // Invalid files are never silently replaced with defaults.
            return JsonSerializer.Deserialize<T>(File.ReadAllText(ConfigPath), JsonOptions) ?? throw new JsonException("Config cannot be null.");
        }
        public void Save<T>(T value) where T : class
        {
            Guard();
            ArgumentNullException.ThrowIfNull(value);
            var text = JsonSerializer.Serialize(value, JsonOptions);
            var temporary = ConfigPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temporary, text); File.Move(temporary, ConfigPath, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public IDisposable Subscribe<T>(Action<T> callback)
        {
            Guard(); ArgumentNullException.ThrowIfNull(callback);
            if (!host.subscriptions.TryGetValue(typeof(T), out var entries)) host.subscriptions[typeof(T)] = entries = new();
            var item = new Subscription(this, callback);
            entries.Add(item);
            Token? token = null;
            token = new Token(host, () => { item.Enabled = false; entries.Remove(item); if (entries.Count == 0) host.subscriptions.Remove(typeof(T)); owned.Remove(token!); });
            owned.Add(token); return token;
        }
        public IDisposable Register(string name, Func<CommandContext, CommandResult> callback)
            => Register(name, callback, "", "");
        public IDisposable Register(string name, Func<CommandContext, CommandResult> callback, string description, string usage)
        {
            Guard(); ArgumentNullException.ThrowIfNull(callback);
            ArgumentNullException.ThrowIfNull(description); ArgumentNullException.ThrowIfNull(usage);
            if (description.Length > 2048 || usage.Length > 1024 || description.Any(c => char.IsControl(c) && c != '\n') || usage.Any(char.IsControl))
                throw new ArgumentException("Description: up to 2048 characters (LF allowed); usage: up to 1024 characters, no control characters.");
            if (!ValidId(name)) throw new ArgumentException("Command names use 3-64 lowercase letters, digits, dots and hyphens.");
            if (name.StartsWith("p90.", StringComparison.Ordinal)) throw new ArgumentException("The p90. command prefix is reserved for the local operator.");
            var entry = new Command(this, callback, description, usage);
            if (!host.commands.TryAdd(name, entry)) throw new InvalidOperationException("Command already registered: " + name);
            Token? token = null;
            token = new Token(host, () => { if (host.commands.TryGetValue(name, out var current) && ReferenceEquals(current, entry)) host.commands.Remove(name); owned.Remove(token!); });
            owned.Add(token); return token;
        }
        public void Dispose()
        {
            host.CheckThread();
            if (!alive) return;
            alive = false;
            foreach (var token in owned.ToArray()) token.Dispose();
            owned.Clear();
        }
    }
}
