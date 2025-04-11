using System.Collections.Generic;

namespace AAEmu.Game.Models.Json;

public class JsonNpcSpawns
{
    public uint Id { get; set; }
    public uint UnitId { get; set; }
    public string Title { get; set; }
    public List<uint> NpcSpawnerIds { get; set; }
    public string FollowPath { get; set; }
    public JsonPosition Position { get; set; }
    public float Scale { get; set; }

}
