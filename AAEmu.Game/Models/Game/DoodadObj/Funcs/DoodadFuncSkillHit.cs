using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncSkillHit : DoodadFuncTemplate
    {
        public uint SkillId { get; set; }
        
        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            // DoodadManager.Instance.TriggerPhases(GetType().Name, caster, owner, SkillId);
            owner.Use(caster, skillId);
        }
    }
}
