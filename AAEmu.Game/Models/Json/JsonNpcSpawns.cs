using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AAEmu.Game.Models.Json
{
    public class JsonNpcSpawns
    {
        public uint Id { get; set; }
        public uint UnitId { get; set; }
        public JsonPosition Position { get; set; }
    }
}
