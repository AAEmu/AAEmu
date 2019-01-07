using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj
{
    public class DoodadFunc
    {
        public uint GroupId { get; set; }
        public uint FuncId { get; set; }
        public string FuncType { get; set; }
        public int NextPhase { get; set; }
        public uint SkillId { get; set; }
        public uint PermId { get; set; }
        public int Count { get; set; }

        public void Use(Unit caster, Doodad owner, uint skillId)
        {

        }
    }
}