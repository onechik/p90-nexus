using P90.API;
namespace P90.Samples.Diagnostics;

[Plugin("sample.diagnostics", "Controlled fault example", "0.1.0")]
public sealed class DiagnosticsPlugin : IP90Plugin
{
    private IPluginContext? context;
    private bool failNext;
    private int pulses;
    public void Start(IPluginContext context)
    {
        this.context = context;
        failNext = false;
        pulses = 0;
        var settings = context.Config.Load<Settings>();
        context.Commands.Register("diagnostic.status", _ => CommandResult.Ok(pulses.ToString()));
        context.Commands.Register("diagnostic.arm", _ => { failNext = settings.AllowControlledFault; return CommandResult.Ok("Armed: " + failNext); });
        context.Commands.Register("diagnostic.fail", _ => throw new InvalidOperationException("Intentional command fault for core verification."));
        context.Events.Subscribe<CorePulse>(_ =>
        {
            pulses++;
            if (failNext) { failNext = false; throw new InvalidOperationException("Intentional event fault for core verification."); }
        });
        context.Log.Info("Diagnostics example started; no fault is injected until requested locally.");
    }
    public void Stop() { context?.Log.Info("Diagnostics example stopped."); context = null; }
    public sealed class Settings { public bool AllowControlledFault { get; set; } = true; }
}
