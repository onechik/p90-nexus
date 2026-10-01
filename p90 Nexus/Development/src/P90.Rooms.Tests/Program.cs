using System.Text.Json;
using P90.Rooms;
var checks=new List<string>();
void Check(string name,Action action){action();checks.Add(name);Console.WriteLine("PASS "+name);}
void Reject(Action action){try{action();}catch(ArgumentException){return;}throw new Exception("Expected validation failure");}
Check("empty-default",()=>new Settings().Validate());
Check("template-roundtrip",()=>{var s=new Settings{Rooms=new(){new RoomDefinition()}};var r=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(s))!;r.Validate();if(r.Rooms[0].Objects.Count!=7)throw new Exception("Template lost");});
Check("duplicate-ids",()=>Reject(()=>new Settings{Rooms=new(){new(){Id="same"},new(){Id="same"}}}.Validate()));
Check("bad-vectors",()=>{var r=new RoomDefinition();r.Entrance.Position=new[]{1f,2f};Reject(()=>new Settings{Rooms=new(){r}}.Validate());r.Entrance.Position=new[]{float.NaN,0f,0f};Reject(()=>new Settings{Rooms=new(){r}}.Validate());});
Check("unknown-schema",()=>Reject(()=>new Settings{SchemaVersion=2}.Validate()));
Check("missing-anchor",()=>Reject(()=>new Settings{Rooms=new(){new(){AnchorRoom=""}}}.Validate()));
Check("invalid-scale",()=>{var r=new RoomDefinition();r.Objects[0].Scale[1]=0;Reject(()=>new Settings{Rooms=new(){r}}.Validate());});
Check("null-collections",()=>Reject(()=>new Settings{Rooms=null!}.Validate()));
Check("invalid-distance-and-cooldown",()=>{Reject(()=>new Settings{InteractionDistance=999}.Validate());Reject(()=>new Settings{CooldownSeconds=0}.Validate());});
Check("many-independent-definitions",()=>new Settings{Rooms=Enumerable.Range(0,1000).Select(i=>new RoomDefinition{Id="room-"+i}).ToList()}.Validate());
Check("l-layout-open-junction",()=>{
    var layout=new RoomLayout{Sections=new(){new(){X=0,Z=0,Width=16,Depth=12},new(){X=4,Z=10,Width=8,Depth=8}}};
    var geometry=layout.Build();
    var floors=geometry.Where(o=>o.Prefab=="Cube_ST"&&o.Position[1]<0).ToArray();
    var area=floors.Sum(o=>o.Scale[0]*o.Scale[2]);
    if(Math.Abs(area-256)>0.01f)throw new Exception("L floor union incorrect");
    if(geometry.Any(o=>o.Position[1]==2&&Math.Abs(o.Position[2]-6)<0.2f&&Math.Abs(o.Position[0]-4)<3.9f))throw new Exception("Wall across junction");
});
Check("overlapping-sections-no-double-floor",()=>{var l=new RoomLayout{Sections=new(){new(),new()}};if(l.Build().Count(o=>o.Position[1]<0)!=1)throw new Exception("Duplicate floor");});
Check("bad-layout",()=>Reject(()=>new RoomLayout{Height=1}.Validate()));
var folder=args.Single();Directory.CreateDirectory(folder);
Check("separate-files-migration-and-preservation",()=>{
    var root=Path.Combine(folder,"store-tests-"+Guid.NewGuid().ToString("N"));var config=Path.Combine(root,"P90","config");Directory.CreateDirectory(config);
    var old=new Settings{Rooms=new(){new(){Id="existing",Entrance=new(){Position=new[]{1f,2f,3f}}}}};
    File.WriteAllText(Path.Combine(config,"p90.rooms.json"),JsonSerializer.Serialize(old));
    var store=new RoomFiles(root);var loaded=store.Load();
    if(!loaded.SeparateRoomFiles||!File.Exists(store.PathFor("existing"))||!Directory.EnumerateFiles(config,"*.bak").Any())throw new Exception("Migration failed");
    var again=store.Load();if(again.Rooms[0].Entrance.Position[1]!=2||again.Rooms[0].Objects.Count!=7)throw new Exception("Legacy changed");
    File.WriteAllText(store.PathFor("existing"),"bad json");
    try{store.Load();throw new Exception("Accepted malformed room");}catch(JsonException){}
});
File.WriteAllText(Path.Combine(folder,"settings-tests.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks},new JsonSerializerOptions{WriteIndented=true}));
