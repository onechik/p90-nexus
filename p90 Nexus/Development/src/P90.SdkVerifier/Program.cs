using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using P90.API;
using P90.Core;

var dll = Path.GetFullPath(args[0]);
var root = Path.GetFullPath(args[1]);
var id = args[2];
var host = new PluginHost(root);
var checks = new List<string>();
void Check(string name, bool value) { if (!value) throw new Exception(name); checks.Add(name); }
try
{
    var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(dll);
    var references = assembly.GetReferencedAssemblies().Select(a => a.Name!).ToArray();
    Check("no-game-or-core-references", references.All(n => n == "P90.API" || n.StartsWith("System.") || n == "netstandard"));
    var types = assembly.GetTypes().Where(t => t.GetCustomAttribute<PluginAttribute>() != null).ToArray();
    Check("one-plugin", types.Length == 1);
    Check("load-independent-binary", host.LoadType(types.Single()).Success);
    Check("command", host.ExecuteLocal(id + ".hello").Message == "Hello, Onechik!");
    Check("no-server-no-broadcast", !host.ExecuteLocal(id + ".announce").Success);
    host.Publish(new PlayerAvailable("test-session", new PlayerSnapshot(0, 1, "Test")));
    Check("subscription", host.ExecuteLocal(id + ".status").Message.Contains("observed=1"));
    host.Publish(new ServerStopped("test-session"));
    Check("session-state-reset", host.ExecuteLocal(id + ".status").Message.Contains("observed=0"));
    for (int i = 0; i < 100; i++)
    {
        CheckRestart();
    }
    checks.Add("100-restarts-without-leaks");
    host.StopAll();
    Check("final-cleanup", host.CommandCount == 0 && host.SubscriptionCount == 0 && host.TrackedRegistrationCount == 0);
    File.WriteAllText(Path.Combine(root, "verification.json"), JsonSerializer.Serialize(new { passed = true, runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, references, checks }, new JsonSerializerOptions { WriteIndented = true }));
    System.Console.WriteLine("SDK binary verified: " + checks.Count + " checks; " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
    return 0;
}
finally { host.StopAll(); }
void CheckRestart()
{
    if (!host.Stop(id).Success || host.TrackedRegistrationCount != 0 || !host.Start(id).Success || host.CommandCount != 3 || host.SubscriptionCount != 2 || host.TrackedRegistrationCount != 5)
        throw new Exception("Restart leaked registrations or failed.");
}
