namespace P90.API;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PluginAttribute : Attribute
{
    public PluginAttribute(string id, string name, string version, int apiMajor = 1)
        => (Id, Name, Version, ApiMajor) = (id, name, version, apiMajor);
    public string Id { get; }
    public string Name { get; }
    public string Version { get; }
    public int ApiMajor { get; }
}

public interface IP90Plugin
{
    void Start(IPluginContext context);
    void Stop();
}

// All context operations, callbacks and token disposal belong to the host thread.
// A context is invalid after stopping its plugin, including after a later restart.
public interface IPluginContext
{
    string PluginId { get; }
    IPluginLog Log { get; }
    IPluginConfig Config { get; }
    IPluginEvents Events { get; }
    IPluginCommands Commands { get; }
    IGameServer Server { get; }
}

public interface IPluginLog
{
    void Info(string message);
    void Error(string message);
}

public interface IPluginConfig
{
    T Load<T>() where T : class, new();
    void Save<T>(T value) where T : class;
}

public interface IPluginEvents
{
    IDisposable Subscribe<T>(Action<T> callback);
}

public interface IPluginCommands
{
    IDisposable Register(string name, Func<CommandContext, CommandResult> callback);
}

// Optional capability: preserves the original IPluginCommands contract for existing DLLs.
public interface IDescribedPluginCommands : IPluginCommands
{
    IDisposable Register(string name, Func<CommandContext, CommandResult> callback, string description, string usage);
}

public static class PluginCommandExtensions
{
    public static IDisposable Register(this IPluginCommands commands, string name,
        Func<CommandContext, CommandResult> callback, string description, string usage)
    {
        if (commands is not IDescribedPluginCommands described)
            throw new NotSupportedException("Command descriptions require an updated P90 host.");
        return described.Register(name, callback, description, usage);
    }
}

public sealed record CommandInfo(string Name, string PluginId, string Description, string Usage);

public sealed record CommandContext(IReadOnlyList<string> Arguments);
public sealed record CommandResult(bool Success, string Message)
{
    public static CommandResult Ok(string message) => new(true, message);
    public static CommandResult Fail(string message) => new(false, message);
}

// A core diagnostic heartbeat, NOT a gameplay event or a server-authority signal.
public sealed record CorePulse(long Number);
