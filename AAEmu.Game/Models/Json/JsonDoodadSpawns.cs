using System.Collections.Generic;

namespace AAEmu.Game.Models.Json
{
    public class JsonDoodadSpawns
    {
        public uint Id { get; set; }
        public uint UnitId { get; set; }
        public List<uint> RelatedIds { get; set; }
        public JsonPosition Position { get; set; }
    }
}
