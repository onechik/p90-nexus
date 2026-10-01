using P90.API;
using P90.DiscoveryFixture.Support;
namespace P90.DiscoveryFixture;
[Plugin("test.folder", "Folder dependency test", "1.0.0")]
public sealed class Plugin : IP90Plugin
{
    public void Start(IPluginContext context) => context.Commands.Register("folder.dependency", _ => CommandResult.Ok(Words.Ready()));
    public void Stop() { }
}
