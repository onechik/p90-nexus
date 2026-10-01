using System.Text.Json;

namespace P90.Rooms;

public sealed class RoomFiles
{
    readonly string global, folder;
    static readonly JsonSerializerOptions Options=new(){WriteIndented=true,PropertyNameCaseInsensitive=true};
    public RoomFiles(string gameRoot){global=Path.Combine(gameRoot,"P90","config","p90.rooms.json");folder=Path.Combine(gameRoot,"P90","config","rooms");}
    public string PathFor(string id)=>Path.Combine(folder,id+".json");
    public Settings Load()
    {
        var settings=File.Exists(global)?JsonSerializer.Deserialize<Settings>(File.ReadAllText(global),Options)??throw new ArgumentException("Пустой общий конфиг."):new Settings();
        settings.Validate();
        if(!settings.SeparateRoomFiles)
        {
            Directory.CreateDirectory(folder);
            // Never overwrite an unrelated room file during migration.
            if(Directory.EnumerateFiles(folder,"*.json").Any())throw new ArgumentException("В папке rooms уже есть JSON. Сначала восстанови общий конфиг с SeparateRoomFiles=true либо убери конфликтующие файлы в резервную папку.");
            if(File.Exists(global))File.Copy(global,global+".before-room-files-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")+".bak");
            Save(settings);
        }
        else
        {
            if(settings.Rooms.Count!=0)throw new ArgumentException("При SeparateRoomFiles=true редактируй отдельные файлы rooms/*.json; общий Rooms должен быть пустым.");
            if(!Directory.Exists(folder))throw new ArgumentException("Отсутствует папка конфигов rooms. Восстанови её из резервной копии.");
            foreach(var file in Directory.EnumerateFiles(folder,"*.json").OrderBy(f=>f))
            {
                var room=JsonSerializer.Deserialize<RoomDefinition>(File.ReadAllText(file),Options)??throw new ArgumentException("Пустой файл "+file);
                if(Path.GetFileNameWithoutExtension(file)!=room.Id)throw new ArgumentException("Имя файла должно совпадать с Id: "+file);
                settings.Rooms.Add(room);
            }
            settings.Validate();
        }
        return settings;
    }
    public void Save(Settings settings)
    {
        settings.Validate();Directory.CreateDirectory(folder);
        foreach(var room in settings.Rooms)Write(PathFor(room.Id),JsonSerializer.Serialize(room,Options));
        var common=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings,Options),Options)!;
        common.Rooms.Clear();common.SeparateRoomFiles=true;
        Write(global,JsonSerializer.Serialize(common,Options));settings.SeparateRoomFiles=true;
    }
    static void Write(string path,string data)
    {
        var temp=path+".tmp";
        File.WriteAllText(temp,data);
        File.Move(temp,path,true);
    }
}
