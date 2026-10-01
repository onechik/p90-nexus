using System.Globalization;
using System.Text.Json;
using HarmonyLib;
using Mirror;
using P90.API;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace P90.Rooms;

[Plugin("p90.rooms", "Custom rooms", "0.2.0")]
public sealed class RoomsPlugin : IP90Plugin
{
    private const string PatchId="onechik.p90.rooms.interaction";
    private static RoomsPlugin? instance;
    private IPluginContext? context;
    private Harmony? harmony;
    private Settings settings=new();
    private RoomFiles files=null!;
    private readonly Dictionary<string,BuiltRoom> built=new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int,Portal> portals=new();
    private readonly Dictionary<uint,Visit> visits=new();
    private readonly Dictionary<uint,float> cooldowns=new();
    private readonly Dictionary<string,string> problems=new();
    private string session="", scene="", signature="";
    private float stableSince;
    private bool attempted;
    private sealed record Portal(BuiltRoom Room, bool Exit);
    private sealed record Visit(GameObject Actor, BuiltRoom Room);
    private sealed class BuiltRoom
    {
        public RoomDefinition Definition=null!;
        public Transform Anchor=null!;
        public Vector3 AnchorPosition;
        public Quaternion AnchorRotation;
        public readonly List<GameObject> Objects=new();
        public GameObject Entry=null!, Exit=null!;
    }
    static Vector3 V(float[] p)=>new(p[0],p[1],p[2]);
    static float[] A(Vector3 p)=>new[]{p.x,p.y,p.z};
    static Quaternion Q(Pose p)=>Quaternion.Euler(V(p.Rotation));
    static Vector3 InteriorPoint(RoomDefinition r,Pose p)=>V(r.Interior.Position)+Q(r.Interior)*V(p.Position);
    static Quaternion InteriorRotation(RoomDefinition r,Pose p)=>Q(r.Interior)*Q(p);
    static Transform AnchorRoot(RoomID r)=>r.transform.parent??r.transform;
    static RoomID[] Anchors()=>Object.FindObjectsOfType<RoomID>().Where(r=>r!=null && r.gameObject.activeInHierarchy).ToArray();
    static SpawnTool Tool()=>Object.FindObjectsOfType<SpawnTool>().FirstOrDefault(t=>t!=null && t.objects!=null)??throw new InvalidOperationException("Дождись появления игрока: SpawnTool ещё не готов.");
    void RequireServer(){if(context==null||!context.Server.Snapshot.Supported||!NetworkServer.lon)throw new InvalidOperationException("Нужен свой активный сервер поддерживаемой сборки.");}
    static string Json(object value)=>JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true});

    public void Start(IPluginContext context)
    {
        this.context=context;
        files=new RoomFiles(Path.GetDirectoryName(Environment.ProcessPath)!);
        settings=files.Load(); settings.Validate();
        // The native process executable is the authoritative game root under the injected runtime.
        var error=P90.GameAdapter.BuildGuard.Verify(Path.GetDirectoryName(Environment.ProcessPath)!);
        if(error.Length!=0)throw new InvalidOperationException(error);
        instance=this;
        harmony=new Harmony(PatchId);
        harmony.Patch(AccessTools.Method(typeof(Door),"bqx"),prefix:new HarmonyMethod(typeof(RoomsPlugin),nameof(OnDoor)));
        context.Events.Subscribe<CorePulse>(_=>Poll());
        context.Events.Subscribe<ServerStopped>(_=>{Clear(false);session="";});
        Register("list",_=>Json(new{settings.Enabled,Ready=built.Keys.ToArray(),Problems=problems,Configured=settings.Rooms.Select(r=>new{r.Id,r.Enabled,r.AnchorRoom,r.AnchorRoot})}),"Список своих комнат, готовность и причины отказа.","rooms.list");
        Register("anchors",CurrentAnchor,"Показывает комнату под ногами указанного игрока и готовую команду создания. ID возьми из players.","rooms.anchors CONNECTION_ID");
        Register("objects",_=>{RequireServer();var t=Tool();return Json(new{Objects=t.objects.Select((o,i)=>new{Index=i,o.name,Prefab=o.prefab.name}),Materials=t.mats.Select((m,i)=>new{Index=i,m.name})});},"Доступные штатные объекты и номера материалов.","rooms.objects");
        Register("where",args=>{RequireServer();Count(args,1,2);var player=Player(args[0]);var matches=args.Count==2?Anchors().Where(r=>r.roomName==args[1]).ToArray():Anchors().OrderBy(r=>Vector3.Distance(AnchorRoot(r).position,player.transform.position)).Take(1).ToArray();if(matches.Length!=1)throw new ArgumentException("Укажи однозначное имя комнаты из rooms.anchors.");var anchor=AnchorRoot(matches[0]);return Json(new{Room=matches[0].roomName,Root=anchor.name,World=A(player.transform.position),Local=A(anchor.InverseTransformPoint(player.transform.position)),LocalRotation=A((Quaternion.Inverse(anchor.rotation)*player.transform.rotation).eulerAngles)});},"Координаты игрока в мире и относительно комнаты. Без имени выбирает ближайшее основание — проверь результат.","rooms.where CONNECTION_ID \"LCZ Office 1\"");
        Register("create",Create,"Создаёт выключенную заготовку комнаты и записывает вход перед выбранным игроком.","rooms.create my-room \"LCZ Office 1\" CONNECTION_ID");
        Register("entry",args=>{RequireServer();Count(args,2);var r=Definition(args[0]);CaptureEntry(r,Player(args[1]));SaveAndReset();return "Вход и точка возврата сохранены; комнаты будут перестроены.";},"Запоминает новое место входа: дверь в двух метрах перед игроком, возврат — на его месте.","rooms.entry my-room CONNECTION_ID");
        Register("enable",args=>Toggle(args,true),"Включает комнату из конфига; после ожидания генерации появятся две двери и объекты.","rooms.enable my-room");
        Register("disable",args=>Toggle(args,false),"Выключает комнату, возвращает вошедших игроков и убирает созданные объекты.","rooms.disable my-room");
        Register("reload",_=>{var next=files.Load();next.Validate();Clear(true);settings=next;Reset();return "Конфиги прочитаны; комнаты будут перестроены.";},"Перечитывает общий конфиг и файлы отдельных комнат. Ошибочный конфиг не заменяет действующие настройки.","rooms.reload");
        Register("file",args=>{Count(args,1);return files.PathFor(Definition(args[0]).Id);},"Показывает полный путь к отдельному конфигу комнаты.","rooms.file my-room");
        Register("layout",args=>{
            Count(args,2);var r=Definition(args[0]);
            if(args[1]!="box"&&args[1]!="l")throw new ArgumentException("Выбери box (16×12 м) или l (L-образная). Например: rooms.layout my-room l");
            var backup=files.PathFor(r.Id)+".before-layout-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")+".bak";
            File.Copy(files.PathFor(r.Id),backup);
            r.Layout=new RoomLayout();if(args[1]=="l")r.Layout.Sections.Add(new Section{X=4,Z=10,Width=8,Depth=8});
            r.Objects=new();r.Arrival=new Pose{Position=new[]{0f,0f,-2f}};r.Exit=new Pose{Position=new[]{0f,0f,-5.7f}};
            r.CaptureHalfSize=new[]{50f,30f,50f};SaveAndReset();
            return "Геометрия заменена заготовкой. Вход на карте сохранён. Прежний файл: "+backup+"\nРедактировать: "+files.PathFor(r.Id);
        },"Заменяет геометрию комнаты заготовкой box или l, сохраняя резервную копию файла. Несохранённые постройки сначала сохрани через rooms.save.","rooms.layout my-room l");
        Register("goto",args=>{RequireServer();Count(args,2);var r=Built(args[0]);Transfer(Player(args[1]),r,false,false);return "Игрок перемещён внутрь комнаты.";},"Переносит указанного игрока в готовую комнату для строительства.","rooms.goto my-room CONNECTION_ID");
        Register("back",args=>{RequireServer();Count(args,1);var p=Player(args[0]);if(!visits.TryGetValue(p.GetComponent<NetworkIdentity>().lmq,out var v))throw new ArgumentException("Для этого персонажа нет сохранённого входа.");Transfer(p,v.Room,true,false);return "Игрок возвращён к входу.";},"Возвращает игрока из комнаты к связанному входу.","rooms.back CONNECTION_ID");
        Register("save",CaptureObjects,"Сохраняет штатные SpawnTool-объекты внутри области комнаты, исключая её портальные двери. Сохранённые объекты переходят под управление плагина.","rooms.save my-room");
        context.Log.Info("Rooms ready. Only explicit door interaction teleports; empty config creates nothing.");
    }
    void Register(string name,Func<IReadOnlyList<string>,string> action,string description,string usage)=>context!.Commands.Register("rooms."+name,c=>{try{return CommandResult.Ok(action(c.Arguments));}catch(Exception e){context.Log.Error(e.ToString());return CommandResult.Fail(e.Message);}},description,usage);
    static void Count(IReadOnlyList<string> a,int min,int max=-1){if(a.Count<min||a.Count>(max<0?min:max))throw new ArgumentException("Неверное число аргументов. Способ использования: com.");}
    RoomDefinition Definition(string id)=>settings.Rooms.SingleOrDefault(r=>r.Id.Equals(id,StringComparison.OrdinalIgnoreCase))??throw new ArgumentException("Нет комнаты "+id);
    BuiltRoom Built(string id)=>built.TryGetValue(id,out var r)?r:throw new ArgumentException("Комната ещё не создана: rooms.list покажет причину.");
    static GameObject Player(string id)
    {
        if(!int.TryParse(id,NumberStyles.Integer,CultureInfo.InvariantCulture,out var n))throw new ArgumentException("Нужен числовой ConnectionId из players.");
        foreach(var pair in NetworkServer.loj)if(pair.Key==n&&pair.Value.llo&&pair.Value.llq!=null)return pair.Value.llq.gameObject;
        throw new ArgumentException("Готовый игрок не найден.");
    }
    string CurrentAnchor(IReadOnlyList<string> args)
    {
        RequireServer();
        if(args.Count!=1)throw new ArgumentException("Введи rooms.anchors ID. Например: rooms.anchors 0. Свой ConnectionId найди командой players.");
        var player=Player(args[0]);
        Physics.SyncTransforms();
        // Identify the actual floor's owning room, never the nearest room origin.
        // Ignore characters and movable construction objects that can cover the map floor.
        foreach(var hit in Physics.RaycastAll(player.transform.position+Vector3.up*0.5f,Vector3.down,4f,~0,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance))
        {
            var collider=hit.collider;
            if(collider==null||collider.GetComponentInParent<PlayerStatistics>()!=null||collider.GetComponentInParent<SpawnedObject>()!=null)continue;
            var found=Anchors().Where(r=>collider.transform.IsChildOf(AnchorRoot(r))).ToArray();
            if(found.Length!=1)break; // An unlabelled/ambiguous floor must not select a room below it.
            var room=found[0];
            return "Комната игрока "+args[0]+": "+room.roomName+"\nДля создания входа здесь:\nrooms.create my-room "+Json(room.roomName)+" "+args[0]+"\nЗамени my-room на своё название новой комнаты. Дверь будет в двух метрах перед персонажем.";
        }
        throw new InvalidOperationException("Не удалось однозначно определить комнату под ногами игрока. Встань на обычный пол внутри комнаты карты, отойди от дверного проёма и повтори rooms.anchors "+args[0]+". В отдельном интерьере или участке без RoomID привязка не определяется.");
    }
    static Transform ResolveAnchor(RoomDefinition r)
    {
        var found=Anchors().Where(a=>a.roomName==r.AnchorRoom&&(r.AnchorRoot.Length==0||AnchorRoot(a).name==r.AnchorRoot)).ToArray();
        if(found.Length!=1)throw new InvalidOperationException($"{r.Id}: найдено оснований {found.Length} ({r.AnchorRoom}). Размещение отменено, чтобы не выбрать другую комнату.");
        var root=AnchorRoot(found[0]);
        if(Vector3.Distance(root.lossyScale,Vector3.one)>0.01f)throw new InvalidOperationException("Масштаб основания не равен 1; требуется отдельная настройка адаптера.");
        return root;
    }
    void CaptureEntry(RoomDefinition r,GameObject p)
    {
        var anchor=ResolveAnchor(r); r.AnchorRoot=anchor.name;
        var yaw=Quaternion.Euler(0,p.transform.eulerAngles.y,0);
        var entry=p.transform.position+yaw*Vector3.forward*2;
        entry.y=Floor(entry,p).y;
        r.Entrance=new Pose{Position=A(anchor.InverseTransformPoint(entry)),Rotation=A((Quaternion.Inverse(anchor.rotation)*yaw).eulerAngles)};
        r.Return=new Pose{Position=A(anchor.InverseTransformPoint(p.transform.position)),Rotation=A((Quaternion.Inverse(anchor.rotation)*yaw).eulerAngles)};
    }
    string Create(IReadOnlyList<string> args)
    {
        RequireServer();Count(args,3);
        if(settings.Rooms.Any(r=>r.Id.Equals(args[0],StringComparison.OrdinalIgnoreCase)))throw new ArgumentException("Этот Id уже существует.");
        var r=new RoomDefinition{Id=args[0],AnchorRoom=args[1],Layout=new RoomLayout(),Objects=new(),Arrival=new Pose{Position=new[]{0f,0f,-2f}},Exit=new Pose{Position=new[]{0f,0f,-5.7f}},CaptureHalfSize=new[]{50f,30f,50f}};
        // No hard-coded number of rooms. The editable default uses separate 100 m slots.
        float x=20000;while(settings.Rooms.Any(old=>Math.Abs(old.Interior.Position[0]-x)<80))x+=100;
        r.Interior.Position=new[]{x,500f,20000f};CaptureEntry(r,Player(args[2]));
        settings.Rooms.Add(r);
        try{settings.Validate();files.Save(settings);}catch{settings.Rooms.Remove(r);throw;}
        return "Заготовка 16×12 м сохранена и выключена. Её конфиг: "+files.PathFor(r.Id)+"\nПосле настройки: rooms.enable "+r.Id;
    }
    string Toggle(IReadOnlyList<string> args,bool enabled){Count(args,1);var r=Definition(args[0]);r.Enabled=enabled;SaveAndReset();return enabled?"Комната включена; ожидается готовность карты.":"Комната выключена.";}
    void SaveAndReset(){settings.Validate();files.Save(settings);Clear(true);Reset();}
    void Reset(){attempted=false;signature="";stableSince=Time.realtimeSinceStartup;problems.Clear();}
    void Poll()
    {
        if(context==null)return;
        try
        {
            var state=context.Server.Snapshot;
            if(!settings.Enabled||!state.IsServer||!NetworkServer.lon){if(built.Count>0)Clear(state.IsServer);return;}
            var currentScene=SceneManager.GetActiveScene().name;
            if(session!=state.SessionId||scene!=currentScene){Clear(false);session=state.SessionId;scene=currentScene;Reset();}
            // Position/rotation changes include seed regeneration even within the same scene.
            var next=string.Join("|",Anchors().OrderBy(r=>r.GetInstanceID()).Select(r=>r.GetInstanceID()+":"+Json(new{P=A(AnchorRoot(r).position),R=A(AnchorRoot(r).eulerAngles)})));
            if(next!=signature){Clear(true);signature=next;stableSince=Time.realtimeSinceStartup;attempted=false;problems.Clear();return;}
            if(attempted||Time.realtimeSinceStartup-stableSince<settings.GenerationSettleSeconds||state.Players.Count==0)return;
            var tool=Tool();attempted=true;
            foreach(var definition in settings.Rooms.Where(r=>r.Enabled))
                try{Build(definition,tool);}catch(Exception e){problems[definition.Id]=e.Message;context.Log.Error(e.ToString());}
            context.Log.Info("Rooms built: "+built.Count+"; refused: "+problems.Count);
        }
        catch(Exception e){context.Log.Error("Rooms poll: "+e);}
    }
    GameObject Spawn(SpawnTool tool,string prefab,Vector3 pos,Quaternion rotation,Vector3 scale,int material,BuiltRoom owner,bool groundDoor=false)
    {
        var indexes=Enumerable.Range(0,tool.objects.Length).Where(i=>tool.objects[i].prefab.name==prefab).ToArray();
        if(indexes.Length!=1)throw new ArgumentException("Неизвестный/неоднозначный Prefab: "+prefab);
        if(material>=tool.mats.Length)throw new ArgumentException("Нет материала "+material);
        var floor=groundDoor?Floor(pos,null):Vector3.zero;
        var g=Object.Instantiate(tool.objects[indexes[0]].prefab,pos,rotation);
        owner.Objects.Add(g);g.transform.localScale=scale;
        if(groundDoor)
        {
            Physics.SyncTransforms();
            var renderers=g.GetComponentsInChildren<Renderer>().Where(r=>r.enabled).ToArray();
            if(renderers.Length==0)throw new InvalidOperationException("У двери нет видимой геометрии.");
            var min=renderers.Min(r=>r.bounds.min.y);
            g.transform.position+=Vector3.up*(floor.y-min+0.01f);
        }
        var spawned=g.GetComponent<SpawnedObject>()??throw new InvalidOperationException("Нет SpawnedObject у "+prefab);
        spawned.mxo=indexes[0];if(material>=0)spawned.mxp=material;
        NetworkServer.hbf(g,null);
        if(g.GetComponent<NetworkIdentity>().lmq==0)throw new InvalidOperationException("Сетевое создание не завершилось: "+prefab);
        return g;
    }
    void Build(RoomDefinition definition,SpawnTool tool)
    {
        var anchor=ResolveAnchor(definition);
        var room=new BuiltRoom{Definition=definition,Anchor=anchor,AnchorPosition=anchor.position,AnchorRotation=anchor.rotation};
        try
        {
            foreach(var obj in (definition.Layout?.Build()??new List<ObjectDefinition>()).Concat(definition.Objects))Spawn(tool,obj.Prefab,InteriorPoint(definition,obj),InteriorRotation(definition,obj),V(obj.Scale),obj.Material,room);
            room.Entry=Spawn(tool,"LCZ_Door_Locked_ST",anchor.TransformPoint(V(definition.Entrance.Position)),anchor.rotation*Q(definition.Entrance),Vector3.one,-1,room,true);
            room.Exit=Spawn(tool,"LCZ_Door_Locked_ST",InteriorPoint(definition,definition.Exit),InteriorRotation(definition,definition.Exit),Vector3.one,-1,room,true);
            Physics.SyncTransforms();
            if(settings.CheckDestination){Destination(InteriorPoint(definition,definition.Arrival),null,true);Destination(anchor.TransformPoint(V(definition.Return.Position)),null,true);}
            portals.Add(room.Entry.GetComponent<Door>().GetInstanceID(),new(room,false));
            portals.Add(room.Exit.GetComponent<Door>().GetInstanceID(),new(room,true));
            built.Add(definition.Id,room);
        }
        catch{Destroy(room);throw;}
    }
    public static bool OnDoor(Door __instance,GameObject __0)
    {
        var self=instance;
        if(self==null||!NetworkServer.lon||__instance==null||!self.portals.TryGetValue(__instance.GetInstanceID(),out var portal))return true;
        try
        {
            if(__0==null)throw new InvalidOperationException("Нет игрока у взаимодействия.");
            if(Vector3.Distance(__0.transform.position,__instance.transform.position)>self.settings.InteractionDistance)throw new InvalidOperationException("Игрок слишком далеко от двери.");
            self.Transfer(__0,portal.Room,portal.Exit,true);
        }
        catch(Exception e){self.context?.Log.Error("Door interaction refused: "+e.Message);}
        return false; // Only our own portal doors suppress normal denied/open behaviour.
    }
    void Transfer(GameObject actor,BuiltRoom room,bool exit,bool useCooldown)
    {
        RequireServer();
        var identity=actor.GetComponent<NetworkIdentity>()??throw new InvalidOperationException("Нет сетевого персонажа.");
        bool owned=false;foreach(var c in NetworkServer.loj)if(c.Value.llo&&c.Value.llq!=null&&c.Value.llq.lmq==identity.lmq){owned=true;break;}
        if(!owned)throw new InvalidOperationException("Персонаж не принадлежит готовому соединению.");
        if(useCooldown&&cooldowns.TryGetValue(identity.lmq,out var until)&&Time.realtimeSinceStartup<until)return;
        if(room.Anchor==null||Vector3.Distance(room.Anchor.position,room.AnchorPosition)>0.01f||Quaternion.Angle(room.Anchor.rotation,room.AnchorRotation)>0.1f)throw new InvalidOperationException("Карта изменилась; дождись перестройки привязок.");
        var p=exit?room.Anchor.TransformPoint(V(room.Definition.Return.Position)):InteriorPoint(room.Definition,room.Definition.Arrival);
        var q=exit?room.Anchor.rotation*Q(room.Definition.Return):InteriorRotation(room.Definition,room.Definition.Arrival);
        p=Destination(p,actor,settings.CheckDestination);
        var movement=actor.GetComponent<NewFirstPersonController>();if(movement!=null)movement.m_MoveDir=Vector3.zero;
        (actor.GetComponent<PlayerTransformSync>()??throw new InvalidOperationException("Нет PlayerTransformSync.")).ckx(p,q);
        cooldowns[identity.lmq]=Time.realtimeSinceStartup+settings.CooldownSeconds;
        if(exit)visits.Remove(identity.lmq);else visits[identity.lmq]=new(actor,room);
        context!.Log.Info($"Teleport: {room.Definition.Id}; player={identity.lmq}; exit={exit}; target={Json(A(p))}");
    }
    static Vector3 Floor(Vector3 p,GameObject? actor)
    {
        Physics.SyncTransforms();
        foreach(var hit in Physics.RaycastAll(p+Vector3.up*0.25f,Vector3.down,4f,~0,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance))
        {
            if(hit.collider.GetComponentInParent<PlayerStatistics>()!=null)continue;
            if(hit.normal.y<0.7f)throw new InvalidOperationException("Под точкой нет подходящего ровного пола.");
            return hit.point;
        }
        throw new InvalidOperationException("Под точкой назначения нет пола в пределах 4 метров.");
    }
    static Vector3 Destination(Vector3 requested,GameObject? actor,bool check)
    {
        var controller=actor?.GetComponent<CharacterController>()??Object.FindObjectsOfType<CharacterController>().FirstOrDefault(c=>c.GetComponent<PlayerStatistics>()!=null);
        if(controller==null)throw new InvalidOperationException("Нет готового персонажа для расчёта безопасной высоты.");
        var scale=controller.transform.lossyScale;
        var height=controller.height*Math.Abs(scale.y);
        var radius=controller.radius*Math.Max(Math.Abs(scale.x),Math.Abs(scale.z));
        var centerY=controller.center.y*scale.y;
        var floor=Floor(requested,actor);
        var p=new Vector3(requested.x,floor.y+height/2-centerY+0.08f,requested.z);
        if(check)
        {
            var center=p+Vector3.up*centerY;
            var half=Math.Max(0,height/2-radius);
            foreach(var c in Physics.OverlapCapsule(center-Vector3.up*half,center+Vector3.up*half,Math.Max(0.05f,radius-0.02f),~0,QueryTriggerInteraction.Ignore))
            {
                var player=c.GetComponentInParent<PlayerStatistics>();
                if(player!=null&&(actor==null||player.gameObject.GetInstanceID()==actor.GetInstanceID()))continue;
                throw new InvalidOperationException("Точка назначения занята геометрией или другим игроком: "+c.name);
            }
        }
        return p;
    }
    string CaptureObjects(IReadOnlyList<string> args)
    {
        RequireServer();Count(args,1);var room=Built(args[0]);var r=room.Definition;var tool=Tool();var half=V(r.CaptureHalfSize);
        var objects=new List<ObjectDefinition>();var adopted=new List<GameObject>();
        foreach(var obj in Object.FindObjectsOfType<SpawnedObject>())
        {
            var g=obj.gameObject;
            if(portals.ContainsKey(g.GetComponent<Door>()?.GetInstanceID()??0))continue;
            if(built.Values.Any(other=>other!=room&&other.Objects.Any(o=>o!=null&&o.GetInstanceID()==g.GetInstanceID())))continue;
            var local=Quaternion.Inverse(Q(r.Interior))*(g.transform.position-V(r.Interior.Position));
            if(Math.Abs(local.x)>half.x||Math.Abs(local.y)>half.y||Math.Abs(local.z)>half.z)continue;
            if(obj.objectIndex<0||obj.objectIndex>=tool.objects.Length)throw new InvalidOperationException("У объекта неизвестный objectIndex.");
            objects.Add(new(){Prefab=tool.objects[obj.objectIndex].prefab.name,Position=A(local),Rotation=A((Quaternion.Inverse(Q(r.Interior))*g.transform.rotation).eulerAngles),Scale=A(g.transform.localScale),Material=obj.materialIndex});
            adopted.Add(g);
        }
        if(objects.Count==0)throw new InvalidOperationException("В области сохранения нет объектов; прежний список оставлен.");
        var previous=r.Objects;var layout=r.Layout;r.Objects=objects;r.Layout=null;
        try{settings.Validate();files.Save(settings);}catch{r.Objects=previous;r.Layout=layout;throw;}
        foreach(var g in adopted)if(!room.Objects.Any(o=>o!=null&&o.GetInstanceID()==g.GetInstanceID()))room.Objects.Add(g);
        return "Сохранено объектов: "+objects.Count+". Двери-порталы сохраняются отдельно в Entrance/Exit.";
    }
    void Destroy(BuiltRoom room)
    {
        if(room.Entry!=null)portals.Remove(room.Entry.GetComponent<Door>().GetInstanceID());
        if(room.Exit!=null)portals.Remove(room.Exit.GetComponent<Door>().GetInstanceID());
        foreach(var g in room.Objects.AsEnumerable().Reverse())if(g!=null){if(NetworkServer.lon&&g.GetComponent<NetworkIdentity>()?.lmq>0)NetworkServer.hbk(g);else Object.Destroy(g);}
        room.Objects.Clear();
    }
    void Clear(bool evacuate)
    {
        if(evacuate&&NetworkServer.lon)
            foreach(var v in visits.Values.ToArray())if(v.Actor!=null&&v.Room.Anchor!=null)
                try{var p=Destination(v.Room.Anchor.TransformPoint(V(v.Room.Definition.Return.Position)),v.Actor,false);v.Actor.GetComponent<PlayerTransformSync>()?.ckx(p,v.Room.Anchor.rotation*Q(v.Room.Definition.Return));}catch(Exception e){context?.Log.Error("Return on cleanup: "+e.Message);}
        visits.Clear();cooldowns.Clear();
        foreach(var room in built.Values)Destroy(room);
        built.Clear();portals.Clear();
    }
    public void Stop()
    {
        try{Clear(true);}finally{harmony?.UnpatchSelf();harmony=null;if(instance==this)instance=null;context=null;Reset();}
    }
}
