using BepInEx;
using Mirror;
using P90.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Reflection;
using System.Text.Json;
using Object=UnityEngine.Object;
namespace P90.RoomProbe;

public sealed class RoomsTest : MonoBehaviour
{
    private int phase, cycle;
    private float since;
    private GameObject? actor;
    private Transform? anchor;
    private readonly List<object> checks=new();
    private readonly List<string> seeds=new();
    private Vector3 firstReturn;
    private uint actorId;
    private int baseline;
    private int initialObjects;
    private PluginHost Host
    {
        get {var type=AppDomain.CurrentDomain.GetAssemblies().First(a=>a.GetName().Name=="P90.Loader").GetType("P90.Loader.CoreLoader")!;var loader=type.GetProperty("Instance",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null);return (PluginHost)type.GetProperty("Host",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(loader)!;}
    }
    public RoomsTest(IntPtr p):base(p){}
    void Next(int p){phase=p;since=Time.realtimeSinceStartup;}
    void Check(string name,bool value){checks.Add(new{name,passed=value,cycle});if(!value)throw new Exception(name);}
    string Command(string name,params string[] args){var r=Host.ExecuteLocal(name,args);if(!r.Success)throw new Exception(name+": "+r.Message);return r.Message;}
    void Write(string file,object value)=>File.WriteAllText(Path.Combine(Paths.GameRootPath,"research/rooms/"+file),JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));
    Door DoorAt(Vector3 p)=>Object.FindObjectsOfType<Door>().Single(d=>Math.Abs(d.transform.position.x-p.x)<0.05f&&Math.Abs(d.transform.position.z-p.z)<0.05f&&Math.Abs(d.transform.position.y-p.y)<2);
    JsonElement Definitions()=>JsonSerializer.SerializeToElement(new{Rooms=new[]{"test-a","test-b"}.Select(id=>JsonDocument.Parse(File.ReadAllText(Path.Combine(Paths.GameRootPath,"P90/config/rooms/"+id+".json"))).RootElement).ToArray()});
    static Vector3 Vec(JsonElement p)=>new(p[0].GetSingle(),p[1].GetSingle(),p[2].GetSingle());
    Vector3 Entry(int i)=>anchor!.TransformPoint(Vec(Definitions().GetProperty("Rooms")[i].GetProperty("Entrance").GetProperty("Position")));
    Vector3 Arrival(int i){var r=Definitions().GetProperty("Rooms")[i];return Vec(r.GetProperty("Interior").GetProperty("Position"))+Vec(r.GetProperty("Arrival").GetProperty("Position"));}
    Vector3 Exit(int i){var r=Definitions().GetProperty("Rooms")[i];return Vec(r.GetProperty("Interior").GetProperty("Position"))+Vec(r.GetProperty("Exit").GetProperty("Position"));}
    void Interact(Door d)=>actor!.GetComponent<PlayerInteract>().chm(d);
    public void Update()
    {
        if(phase==99)return;
        try
        {
            if(phase!=0 && Time.realtimeSinceStartup-since>65)throw new TimeoutException("rooms test phase "+phase+" "+Command("rooms.list"));
            if(phase==0&&SceneManager.GetActiveScene().name=="MainMenu"&&Time.realtimeSinceStartup>4){StartHost();return;}
            if(phase==1&&SceneManager.GetActiveScene().name=="Facility"&&NetworkServer.lon&&Time.realtimeSinceStartup-since>17)
            {
                var room=Object.FindObjectsOfType<RoomID>().Single(r=>r.roomName=="LCZ Office 1");anchor=room.transform.parent;
                actor=Object.FindObjectsOfType<PlayerInteract>().First(p=>p.GetComponent<NetworkIdentity>().lmq!=0).gameObject;actorId=actor.GetComponent<NetworkIdentity>().lmq;
                seeds.Add(string.Join(",",Object.FindObjectsOfType<ZoneGenerator>().Select(z=>z.name+"="+z._key)));
                if(cycle==0)
                {
                    baseline=Object.FindObjectsOfType<SpawnedObject>().Length;
                    // The game's room marker is its admin teleport destination. Find ground under it.
                    var p=room.transform.position;
                    if(Physics.Raycast(p,Vector3.down,out var hit,12,~0,QueryTriggerInteraction.Ignore))p=hit.point+Vector3.up*1.6f;
                    actor.GetComponent<PlayerTransformSync>().ckx(p,Quaternion.identity);firstReturn=p;
                    var anchorReply=Command("rooms.anchors","0");
                    Check("current-anchor-name",anchorReply.Contains("LCZ Office 1"));
                    Check("current-anchor-create-example",anchorReply.Contains("rooms.create my-room \"LCZ Office 1\" 0"));
                    Check("anchor-requires-player",!Host.ExecuteLocal("rooms.anchors").Success);
                    Check("anchor-rejects-unknown-player",!Host.ExecuteLocal("rooms.anchors",new[]{"999999"}).Success);
                    actor.GetComponent<PlayerTransformSync>().ckx(new Vector3(40000,500,40000),Quaternion.identity);
                    Check("anchor-refuses-void",!Host.ExecuteLocal("rooms.anchors",new[]{"0"}).Success);
                    actor.GetComponent<PlayerTransformSync>().ckx(p,Quaternion.identity);
                    Command("rooms.create","test-a","LCZ Office 1","0");
                    Check("separate-room-file",File.Exists(Command("rooms.file","test-a")));
                    // Second entrance near the same room, separate identity and configured destination.
                    actor.GetComponent<PlayerTransformSync>().ckx(p,Quaternion.Euler(0,180,0));
                    Command("rooms.create","test-b","LCZ Office 1","0");
                    Command("rooms.layout","test-b","l");
                    Check("layout-backup",Directory.EnumerateFiles(Path.Combine(Paths.GameRootPath,"P90/config/rooms"),"test-b.json.before-layout-*.bak").Any());
                    actor.GetComponent<PlayerTransformSync>().ckx(firstReturn,Quaternion.identity);
                    Command("rooms.enable","test-a");Command("rooms.enable","test-b");
                }
                Next(2);return;
            }
            if(phase==2&&Time.realtimeSinceStartup-since>8)
            {
                var status=JsonDocument.Parse(Command("rooms.list")).RootElement;
                Write("build-status.json",status);
                Check("two-ready-rooms",status.GetProperty("Ready").GetArrayLength()==2);
                if(cycle==0){
                    initialObjects=Object.FindObjectsOfType<SpawnedObject>().Length;
                    var interior=Vec(Definitions().GetProperty("Rooms")[1].GetProperty("Interior").GetProperty("Position"));
                    Check("l-wing-floor",Physics.Raycast(interior+new Vector3(4,2,10),Vector3.down,out var wingHit,3,~0,QueryTriggerInteraction.Ignore)&&Math.Abs(wingHit.point.y-interior.y)<0.05f);
                    Check("l-cutout-empty",!Physics.Raycast(interior+new Vector3(-4,2,10),Vector3.down,out var cutoutHit,3,~0,QueryTriggerInteraction.Ignore));
                    Check("l-junction-open",!Physics.Raycast(interior+new Vector3(4,1,5),Vector3.forward,out var junctionHit,2,~0,QueryTriggerInteraction.Ignore));
                }
                Check("independent-door-identities",DoorAt(Entry(0)).GetComponent<NetworkIdentity>().lmq!=DoorAt(Entry(1)).GetComponent<NetworkIdentity>().lmq);
                var entryDoor=DoorAt(Entry(0));
                Check("anchor-transform-on-seed",Math.Abs(entryDoor.transform.position.x-Entry(0).x)<0.01f&&Math.Abs(entryDoor.transform.position.z-Entry(0).z)<0.01f);
                var doorBottom=entryDoor.GetComponentsInChildren<Renderer>().Min(r=>r.bounds.min.y);
                Check("door-bottom-aligned",Math.Abs(doorBottom-(firstReturn.y-1.6f))<0.06f);
                if(cycle>0){FinishCycle();return;}
                actor!.GetComponent<PlayerTransformSync>().ckx(firstReturn,Quaternion.identity);
                Next(3);return;
            }
            if(phase==3&&Time.realtimeSinceStartup-since>1)
            {
                Check("no-teleport-without-click",Vector3.Distance(actor!.transform.position,Arrival(0))>100);
                Interact(DoorAt(Entry(0)));Next(4);return;
            }
            if(phase==4&&Time.realtimeSinceStartup-since>0.25f)
            {
                Check("entry-command-teleports",Vector3.Distance(actor!.transform.position,Arrival(0))<2);
                Check("same-player",actor.GetComponent<NetworkIdentity>().lmq==actorId);
                Interact(DoorAt(Exit(0)));Next(5);return;
            }
            if(phase==5&&Time.realtimeSinceStartup-since>0.25f)
            {
                Check("cooldown-prevents-bounce",Vector3.Distance(actor!.transform.position,Arrival(0))<2);
                Next(6);return;
            }
            if(phase==6&&Time.realtimeSinceStartup-since>4){
                var cc=actor!.GetComponent<CharacterController>();
                var feet=actor.transform.position.y+cc.center.y-cc.height/2;
                var floor=Definitions().GetProperty("Rooms")[0].GetProperty("Interior").GetProperty("Position")[1].GetSingle();
                Check("feet-remain-above-room-floor",feet>=floor-0.08f&&feet<floor+0.3f);
                Interact(DoorAt(Exit(0)));Next(7);return;}
            if(phase==7&&Time.realtimeSinceStartup-since>0.25f)
            {
                Check("exit-returns-to-anchor",Vector3.Distance(actor!.transform.position,firstReturn)<2);
                actor.GetComponent<PlayerTransformSync>().ckx(firstReturn,Quaternion.identity);Next(8);return;
            }
            if(phase==8&&Time.realtimeSinceStartup-since>2){Interact(DoorAt(Entry(1)));Next(9);return;}
            if(phase==9&&Time.realtimeSinceStartup-since>0.25f)
            {
                Check("second-portal-own-destination",Vector3.Distance(actor!.transform.position,Arrival(1))<2);
                Command("rooms.back","0");Command("rooms.goto","test-a","0");
                var tool=Object.FindObjectsOfType<SpawnTool>().First();var extra=Object.Instantiate(tool.objects[0].prefab,Arrival(0)+new Vector3(2,0.5f,2),Quaternion.identity);extra.transform.localScale=new(2,1,2);extra.GetComponent<SpawnedObject>().mxo=0;extra.GetComponent<SpawnedObject>().mxp=6;NetworkServer.hbf(extra,null);
                Command("rooms.save","test-a");Check("capture-added-geometry",Definitions().GetProperty("Rooms")[0].GetProperty("Objects").GetArrayLength()==8);
                Command("rooms.reload");Next(10);return;
            }
            if(phase==10&&Time.realtimeSinceStartup-since>8)
            {
                Check("reload-without-duplicates",Object.FindObjectsOfType<SpawnedObject>().Length==initialObjects+1);
                Check("stop-success",Host.Stop("p90.rooms").Success);Next(11);return;
            }
            if(phase==11&&Time.realtimeSinceStartup-since>1)
            {
                Check("stop-removes-owned-objects",Object.FindObjectsOfType<SpawnedObject>().Length==baseline);
                Check("stop-removes-commands",!Host.ExecuteLocal("rooms.list").Success);
                Check("restart-success",Host.Start("p90.rooms").Success);FinishCycle();return;
            }
            if(phase==20&&SceneManager.GetActiveScene().name=="MainMenu"&&Time.realtimeSinceStartup-since>4){StartHost();return;}
        }
        catch(Exception e){Write("rooms-test-result.json",new{passed=false,phase,cycle,error=e.ToString(),checks,seeds});phase=99;Application.Quit(1);}
    }
    void StartHost(){UnityEngine.Random.InitState(7701+cycle*172);NetworkServer.lom=false;NetworkManager.lnh.onlineScene="Facility";Next(1);NetworkManager.lnh.gsr();}
    void FinishCycle()
    {
        cycle++;
        if(cycle>=3){Check("different-seeds",seeds.Distinct().Count()>=2);Write("rooms-test-result.json",new{passed=true,checks,seeds});phase=99;Application.Quit(0);return;}
        NetworkManager.lnh.gst();Next(20);
    }
}
