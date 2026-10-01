using P90.API;

namespace __NAME__;

[Plugin("__ID__", "__NAME__", "0.1.0", apiMajor: 1)]
public sealed class ExamplePlugin : IP90Plugin
{
    private IPluginContext? context;
    private int observedPlayers;

    public void Start(IPluginContext context)
    {
        this.context = context;
        // Start may run repeatedly on the same instance. Reset transient state.
        observedPlayers = context.Server.Snapshot.Players.Count;
        var settings = context.Config.Load<Settings>();
        context.Commands.Register("__ID__.hello", _ => CommandResult.Ok(settings.Greeting), description: "Показывает приветствие из настроек.", usage: "__ID__.hello");
        context.Commands.Register("__ID__.status", _ => CommandResult.Ok($"Server={context.Server.Snapshot.IsServer}; observed={observedPlayers}"), description: "Показывает состояние сервера и число наблюдений игроков.", usage: "__ID__.status");
        context.Commands.Register("__ID__.announce", args => context.Server.Broadcast(args.Arguments.Count == 0 ? settings.Greeting : string.Join(" ", args.Arguments)), description: "Отправляет объявление на своём сервере.", usage: "__ID__.announce Текст объявления");
        context.Events.Subscribe<PlayerAvailable>(_ => observedPlayers++);
        context.Events.Subscribe<ServerStopped>(_ => observedPlayers = 0);
        context.Log.Info("Example started.");
    }

    public void Stop()
    {
        context?.Log.Info("Example stopped.");
        context = null;
        // The host releases context registrations. Release your own resources here.
    }

    public sealed class Settings
    {
        public string Greeting { get; set; } = "Hello, Onechik!";
    }
}
