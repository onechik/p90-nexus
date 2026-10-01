using System.Text.Json;
using P90.API;
namespace P90.Samples.ServerDiagnostics;

[Plugin("sample.server", "Server diagnostics", "0.2.0")]
public sealed class ServerDiagnosticsPlugin : IP90Plugin
{
    private IPluginContext? context;
    public void Start(IPluginContext context)
    {
        this.context = context;
        var settings = context.Config.Load<Settings>();
        context.Commands.Register("server.status", _ => CommandResult.Ok(JsonSerializer.Serialize(context.Server.Snapshot)),
            description: "Показывает состояние сервера, сцену и количество готовых игроков.", usage: "server.status");
        context.Commands.Register("server.players", _ => CommandResult.Ok(JsonSerializer.Serialize(context.Server.Snapshot.Players)),
            description: "Показывает список готовых игроков и их сетевые ID.", usage: "server.players");
        context.Commands.Register("server.announce", command => context.Server.Broadcast(command.Arguments.Count == 0 ? settings.DefaultAnnouncement : string.Join(" ", command.Arguments)),
            description: "Показывает объявление на своём сервере. Без текста использует DefaultAnnouncement из конфига.", usage: "server.announce Привет всем!");
        context.Commands.Register("server.clear", _ => context.Server.Broadcast(""),
            description: "Убирает текущее объявление на своём сервере.", usage: "server.clear");
        context.Events.Subscribe<ServerStarted>(e => context.Log.Info("Server started: " + e.SessionId));
        context.Events.Subscribe<ServerStopped>(e => context.Log.Info("Server stopped: " + e.SessionId));
        context.Events.Subscribe<PlayerAvailable>(e => context.Log.Info("Player available: " + JsonSerializer.Serialize(e)));
        context.Events.Subscribe<PlayerUnavailable>(e => context.Log.Info("Player unavailable: " + JsonSerializer.Serialize(e)));
        context.Log.Info("Server diagnostics ready. Announcements require an explicit local command.");
    }
    public void Stop() { context?.Log.Info("Server diagnostics stopped."); context = null; }
    public sealed class Settings { public string DefaultAnnouncement { get; set; } = "P90: привет, Onechik! Проверка серверного плагина."; }
}
