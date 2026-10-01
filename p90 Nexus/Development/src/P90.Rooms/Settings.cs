namespace P90.Rooms;

public sealed class Settings
{
    public int SchemaVersion { get; set; } = 1;
    public bool Enabled { get; set; } = true;
    public float GenerationSettleSeconds { get; set; } = 4;
    public float InteractionDistance { get; set; } = 4;
    public float CooldownSeconds { get; set; } = 1.5f;
    public bool CheckDestination { get; set; } = true;
    public bool SeparateRoomFiles { get; set; }
    public List<RoomDefinition> Rooms { get; set; } = new();

    public void Validate()
    {
        if (SchemaVersion != 1) throw new ArgumentException("Поддерживается SchemaVersion=1.");
        Range(GenerationSettleSeconds, 2, 60, nameof(GenerationSettleSeconds));
        Range(InteractionDistance, 1, 6, nameof(InteractionDistance));
        Range(CooldownSeconds, 0.5f, 60, nameof(CooldownSeconds));
        if (Rooms == null) throw new ArgumentException("Rooms не может быть null.");
        var ids=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var room in Rooms)
        {
            if(room==null || !System.Text.RegularExpressions.Regex.IsMatch(room.Id??"", "\\A[a-z][a-z0-9.-]{0,63}\\z") || !ids.Add(room.Id!)) throw new ArgumentException("У каждой комнаты нужен уникальный Id: латинские буквы, цифры, точка, дефис.");
            if(string.IsNullOrWhiteSpace(room.AnchorRoom)) throw new ArgumentException(room.Id+": укажи AnchorRoom из rooms.anchors.");
            if(room.AnchorRoot==null) throw new ArgumentException(room.Id+": AnchorRoot не может быть null.");
            Pose(room.Entrance, room.Id+" Entrance"); Pose(room.Return, room.Id+" Return");
            Pose(room.Interior, room.Id+" Interior"); Pose(room.Arrival, room.Id+" Arrival"); Pose(room.Exit, room.Id+" Exit");
            Vec(room.CaptureHalfSize,room.Id+" CaptureHalfSize");
            foreach(var v in room.CaptureHalfSize) Range(v,1,200,"CaptureHalfSize");
            if(room.Objects==null) throw new ArgumentException(room.Id+": Objects не может быть null.");
            room.Layout?.Validate();
            foreach(var obj in room.Objects)
            {
                if(obj==null || string.IsNullOrWhiteSpace(obj.Prefab)) throw new ArgumentException(room.Id+": нужен Prefab.");
                Pose(obj,room.Id+" object"); Vec(obj.Scale,room.Id+" Scale");
                foreach(var v in obj.Scale) Range(v,0.01f,1000,"Scale");
                if(obj.Material < -1) throw new ArgumentException("Material должен быть -1 или индексом из rooms.objects.");
            }
        }
    }
    static void Range(float v,float min,float max,string field) { if(!float.IsFinite(v)||v<min||v>max) throw new ArgumentException(field+": допустимо "+min+"…"+max); }
    static void Vec(float[]? v,string field) { if(v==null||v.Length!=3||v.Any(x=>!float.IsFinite(x)||Math.Abs(x)>100000)) throw new ArgumentException(field+": нужны три конечных числа в пределах ±100000."); }
    static void Pose(Pose? p,string field) { if(p==null) throw new ArgumentException(field+": null"); Vec(p.Position,field); Vec(p.Rotation,field); }
}
public class Pose
{
    public float[] Position { get; set; } = new float[3];
    public float[] Rotation { get; set; } = new float[3];
}
public sealed class ObjectDefinition : Pose
{
    public string Prefab { get; set; } = "Cube_ST";
    public float[] Scale { get; set; } = new[] {1f,1f,1f};
    public int Material { get; set; } = -1;
}
public sealed class RoomDefinition
{
    public string Id { get; set; } = "my-room";
    public bool Enabled { get; set; } = false;
    public string AnchorRoom { get; set; } = "LCZ Office 1";
    public string AnchorRoot { get; set; } = "";
    public Pose Entrance { get; set; } = new();
    public Pose Return { get; set; } = new();
    public Pose Interior { get; set; } = new() {Position=new[]{20000f,500f,20000f}};
    public Pose Arrival { get; set; } = new() {Position=new[]{0f,0.2f,-1.5f}};
    public Pose Exit { get; set; } = new() {Position=new[]{0f,0f,-3.7f}};
    public float[] CaptureHalfSize { get; set; } = new[]{15f,10f,15f};
    public List<ObjectDefinition> Objects { get; set; } = Box();
    public RoomLayout? Layout { get; set; }
    public static List<ObjectDefinition> Box() => new()
    {
        new(){Position=new[]{0f,-0.25f,0f},Scale=new[]{8f,0.5f,8f},Material=10},
        new(){Position=new[]{-4f,1.75f,0f},Scale=new[]{0.3f,3.5f,8f},Material=12},
        new(){Position=new[]{4f,1.75f,0f},Scale=new[]{0.3f,3.5f,8f},Material=12},
        new(){Position=new[]{0f,1.75f,4f},Scale=new[]{8f,3.5f,0.3f},Material=12},
        new(){Position=new[]{0f,1.75f,-4f},Scale=new[]{8f,3.5f,0.3f},Material=12},
        new(){Position=new[]{0f,3.5f,0f},Scale=new[]{8f,0.3f,8f},Material=12},
        new(){Prefab="Light_ST",Position=new[]{0f,3f,0f}}
    };
}
