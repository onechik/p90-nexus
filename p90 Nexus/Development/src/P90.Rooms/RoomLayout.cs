namespace P90.Rooms;

public sealed class Section
{
    public float X { get; set; }
    public float Z { get; set; }
    public float Width { get; set; } = 16;
    public float Depth { get; set; } = 12;
}

public sealed class RoomLayout
{
    public float Height { get; set; } = 4;
    public float WallThickness { get; set; } = 0.3f;
    public float FloorThickness { get; set; } = 1;
    public int FloorMaterial { get; set; } = 10;
    public int WallMaterial { get; set; } = 12;
    public int CeilingMaterial { get; set; } = 12;
    public bool Ceiling { get; set; } = true;
    public bool Lights { get; set; } = true;
    public List<Section> Sections { get; set; } = new(){new()};

    public void Validate()
    {
        void Range(float x,float min,float max){if(!float.IsFinite(x)||x<min||x>max)throw new ArgumentException("Layout: размер вне диапазона "+min+"…"+max);}
        Range(Height,3.5f,30);Range(WallThickness,0.1f,2);Range(FloorThickness,0.3f,5);
        if(FloorMaterial < -1||WallMaterial < -1||CeilingMaterial < -1)throw new ArgumentException("Layout: неверный материал.");
        if(Sections==null||Sections.Count==0||Sections.Count>32)throw new ArgumentException("Layout: нужно от 1 до 32 секций.");
        foreach(var s in Sections){if(s==null)throw new ArgumentException("Layout: пустая секция.");Range(s.X,-150,150);Range(s.Z,-150,150);Range(s.Width,3,100);Range(s.Depth,3,100);}
    }

    public List<ObjectDefinition> Build()
    {
        Validate();
        var xs=Sections.SelectMany(s=>new[]{s.X-s.Width/2,s.X+s.Width/2}).Distinct().OrderBy(x=>x).ToArray();
        var zs=Sections.SelectMany(s=>new[]{s.Z-s.Depth/2,s.Z+s.Depth/2}).Distinct().OrderBy(x=>x).ToArray();
        var occupied=new bool[xs.Length-1,zs.Length-1];
        for(int x=0;x<xs.Length-1;x++)for(int z=0;z<zs.Length-1;z++)
        {
            var cx=(xs[x]+xs[x+1])/2;var cz=(zs[z]+zs[z+1])/2;
            occupied[x,z]=Sections.Any(s=>Math.Abs(cx-s.X)<s.Width/2&&Math.Abs(cz-s.Z)<s.Depth/2);
        }
        var result=new List<ObjectDefinition>();
        void Cube(float x,float y,float z,float w,float h,float d,int material)=>result.Add(new(){Position=new[]{x,y,z},Scale=new[]{w,h,d},Material=material});
        for(int x=0;x<xs.Length-1;x++)for(int z=0;z<zs.Length-1;z++)if(occupied[x,z])
        {
            float w=xs[x+1]-xs[x],d=zs[z+1]-zs[z],cx=(xs[x]+xs[x+1])/2,cz=(zs[z]+zs[z+1])/2;
            Cube(cx,-FloorThickness/2,cz,w,FloorThickness,d,FloorMaterial);
            if(Ceiling)Cube(cx,Height+WallThickness/2,cz,w,WallThickness,d,CeilingMaterial);
            if(x==0||!occupied[x-1,z])Cube(xs[x]-WallThickness/2,Height/2,cz,WallThickness,Height,d,WallMaterial);
            if(x==xs.Length-2||!occupied[x+1,z])Cube(xs[x+1]+WallThickness/2,Height/2,cz,WallThickness,Height,d,WallMaterial);
            if(z==0||!occupied[x,z-1])Cube(cx,Height/2,zs[z]-WallThickness/2,w,Height,WallThickness,WallMaterial);
            if(z==zs.Length-2||!occupied[x,z+1])Cube(cx,Height/2,zs[z+1]+WallThickness/2,w,Height,WallThickness,WallMaterial);
        }
        if(Lights)foreach(var s in Sections)result.Add(new(){Prefab="Light_ST",Position=new[]{s.X,Height-0.5f,s.Z}});
        return result;
    }
}
