using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncFakeUse : DoodadFuncTemplate
    {
        public uint SkillId { get; set; }
        public uint FakeSkillId { get; set; }
        public bool TargetParent { get; set; }
        
        public override void Use(Unit caster, Doodad owner, uint skillId)
        {
            _log.Debug("DoodadFuncFakeUse");
        }
    }
}
