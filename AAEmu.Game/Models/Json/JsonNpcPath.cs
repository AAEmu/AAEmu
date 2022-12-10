using System.Collections.Generic;

namespace AAEmu.Game.Models.Json
{
    public class JsonNpcPath
    {
        public uint Id { get; set; }
        public uint UnitId { get; set; }
        public uint SkillId { get; set; }
        public bool Cycle{ get; set; }
        public List<JsonPosition> Positions { get; set; }
    }
}
