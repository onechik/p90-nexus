using P90.API;
namespace P90.Samples.Lifecycle;

[Plugin("sample.lifecycle", "Lifecycle example", "0.1.0")]
public sealed class LifecyclePlugin : IP90Plugin
{
    private IPluginContext? context;
    private int pulses;
    public void Start(IPluginContext context)
    {
        this.context = context;
        pulses = 0;
        var config = context.Config.Load<Settings>();
        context.Commands.Register("sample.hello", _ => CommandResult.Ok(config.Greeting));
        context.Commands.Register("sample.pulses", _ => CommandResult.Ok(pulses.ToString()));
        context.Events.Subscribe<CorePulse>(_ => pulses++);
        context.Log.Info("Lifecycle example started.");
    }
    public void Stop() { context?.Log.Info($"Lifecycle example stopped after {pulses} pulses."); context = null; }
    public sealed class Settings { public string Greeting { get; set; } = "Hello, Onechik!"; }
}
