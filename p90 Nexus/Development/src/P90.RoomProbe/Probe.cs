using BepInEx;
using BepInEx.Unity.IL2CPP;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Text.Json;
using HarmonyLib;
using Object = UnityEngine.Object;
namespace P90.RoomProbe;

[BepInPlugin("onechik.p90.roomprobe", "P90 room research", "0.1.0")]
public sealed class Probe : BasePlugin
{
    public override void Load()
    {
        if (Environment.GetCommandLineArgs().Contains("--p90-room-probe")) AddComponent<ProbeBehaviour>();
        if (Environment.GetCommandLineArgs().Contains("--p90-rooms-test")) AddComponent<RoomsTest>();
    }
}
public sealed class ProbeBehaviour : MonoBehaviour
{
    private int phase;
    private float since;
    private GameObject? actor, first, second;
    private Vector3 target;
    public static int Hits;
    public static uint ActorId;
    public static Door? Expected;
    public static Vector3 Destination;
    public static bool DoorPrefix(Door __instance, GameObject __0)
    {
        if (!NetworkServer.lon || Expected == null || __instance.GetInstanceID() != Expected.GetInstanceID()) return true;
        Hits++; ActorId=__0.GetComponent<NetworkIdentity>().lmq;
        __0.GetComponent<PlayerTransformSync>().ckx(Destination,Quaternion.identity);
        return false;
    }
    public ProbeBehaviour(IntPtr p) : base(p) { }
    static float[] V(Vector3 v) => new[] {v.x,v.y,v.z};
    static string[] Components(GameObject g) => g.GetComponentsInChildren<Component>(true).Select(c => c == null ? "null" : c.GetIl2CppType().FullName).Distinct().ToArray();
    static object Shape(GameObject g) => new {g.name, tag=g.tag, pos=V(g.transform.position), rot=V(g.transform.eulerAngles), scale=V(g.transform.localScale), components=Components(g)};
    public void Update()
    {
        try
        {
            if (phase == 0 && SceneManager.GetActiveScene().name == "MainMenu" && Time.realtimeSinceStartup > 4)
            {
                NetworkServer.lom=false; NetworkManager.lnh.onlineScene="Facility"; phase=1; since=Time.realtimeSinceStartup; NetworkManager.lnh.gsr();
            }
            if (phase == 1 && NetworkServer.lon && SceneManager.GetActiveScene().name == "Facility" && Time.realtimeSinceStartup - since > 18)
            {
                var tools=Object.FindObjectsOfType<SpawnTool>(true);
                var rooms=Object.FindObjectsOfType<RoomID>(true);
                var output=new {
                    scene=SceneManager.GetActiveScene().name,
                    tools=tools.Select(t=> new { t.name, objects=t.objects.Select((o,i)=>new {index=i,o.name, prefab=Shape(o.prefab)}).ToArray(), materials=t.mats.Select((m,i)=>new {index=i,m.name}).ToArray()}).ToArray(),
                    rooms=rooms.Select(r=>new {r.roomName, prefab=Shape(r.gameObject),parent=r.transform.parent?.name}).ToArray(),
                    doors=Object.FindObjectsOfType<Door>(true).Select(d=>new {d.name,d.AccessLevel, net=d.GetComponent<NetworkIdentity>()?.lmq,pos=V(d.transform.position),parent=d.transform.parent?.name}).ToArray(),
                    zones=Object.FindObjectsOfType<ZoneGenerator>(true).Select(z=>new {z.name,z.zoneHeight,z._key,available=Enumerable.Range(0,z.availableRooms.Count).Select(i=>z.availableRooms[i]).Select(r=>new {type=r.type.ToString(),r.required,rooms=Enumerable.Range(0,r.room.Count).Select(i=>r.room[i].name).ToArray()}).ToArray()}).ToArray()
                };
                File.WriteAllText(Path.Combine(Paths.GameRootPath,"research/rooms/runtime-inspection.json"),JsonSerializer.Serialize(output,new JsonSerializerOptions{WriteIndented=true}));
                actor=Object.FindObjectsOfType<PlayerInteract>().First(p=>p.GetComponent<NetworkIdentity>().lmq!=0).gameObject;
                var tool=tools.First();
                first=Object.Instantiate(tool.objects[3].prefab,actor.transform.position+Vector3.right*3,Quaternion.identity);
                first.GetComponent<SpawnedObject>().mxo=3; NetworkServer.hbf(first,null);
                second=Object.Instantiate(tool.objects[3].prefab,actor.transform.position+Vector3.left*3,Quaternion.identity);
                second.GetComponent<SpawnedObject>().mxo=3; NetworkServer.hbf(second,null);
                Physics.SyncTransforms();
                File.WriteAllText(Path.Combine(Paths.GameRootPath,"research/rooms/physics-shapes.json"),JsonSerializer.Serialize(new {
                    player=Shape(actor),
                    playerColliders=actor.GetComponentsInChildren<Collider>().Select(c=>new {type=c.GetIl2CppType().Name,c.name,c.isTrigger,min=V(c.bounds.min),max=V(c.bounds.max)}).ToArray(),
                    controllers=actor.GetComponentsInChildren<CharacterController>().Select(c=>new {c.height,c.radius,center=V(c.center),c.skinWidth}).ToArray(),
                    door=Shape(first),doorColliders=first.GetComponentsInChildren<Collider>().Select(c=>new{c.name,c.isTrigger,min=V(c.bounds.min),max=V(c.bounds.max)}).ToArray(),
                    renderers=first.GetComponentsInChildren<Renderer>().Select(r=>new{r.name,min=V(r.bounds.min),max=V(r.bounds.max)}).ToArray()
                },new JsonSerializerOptions{WriteIndented=true}));
                Expected=first.GetComponent<Door>(); Destination=actor.transform.position+Vector3.up*2; target=Destination;
                new Harmony("onechik.p90.roomprobe").Patch(AccessTools.Method(typeof(Door),"bqx"),prefix:new HarmonyMethod(typeof(ProbeBehaviour),nameof(DoorPrefix)));
                since=Time.realtimeSinceStartup; phase=2;
            }
            if(phase==2 && Time.realtimeSinceStartup-since>1) {
                if(Hits!=0) throw new Exception("Door hook activated without interaction.");
                actor!.GetComponent<PlayerInteract>().chm(Expected);
                since=Time.realtimeSinceStartup;phase=3;
            }
            if(phase==3 && Time.realtimeSinceStartup-since>0.2f) {
                var result=new { Hits, ActorId,expectedActor=actor!.GetComponent<NetworkIdentity>().lmq,first=first!.GetComponent<NetworkIdentity>().lmq,second=second!.GetComponent<NetworkIdentity>().lmq,target=V(target),actual=V(actor.transform.position) };
                File.WriteAllText(Path.Combine(Paths.GameRootPath,"research/rooms/door-hook.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
                if(Hits!=1 || ActorId!=result.expectedActor || result.first==0 || result.second==0 || result.first==result.second || Vector3.Distance(target,actor.transform.position)>2) throw new Exception("Door network hook / teleport failed");
                phase=4;Application.Quit(0);
            }
        }
        catch(Exception e) {File.WriteAllText(Path.Combine(Paths.GameRootPath,"research/rooms/probe-error.txt"),e.ToString());phase=2;Application.Quit(1);}
    }
}
