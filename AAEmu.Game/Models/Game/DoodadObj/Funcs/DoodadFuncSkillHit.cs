using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncSkillHit : DoodadFuncTemplate
{
    public uint SkillId { get; set; }

    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        // SkillHit is a phase gate: the incoming skill either matches this row or it does not.
        // Recasting the skill / creating items from SpecialEffect.Value1 was leftover harvest
        // loot logic and re-entered OnSkillHit (chum never needed it).
        owner.ToNextPhase = AdvancesPhase(SkillId, skillId);
    }

    public static bool AdvancesPhase(uint hitSkillId, uint incomingSkillId) =>
        hitSkillId != 0 && hitSkillId == incomingSkillId;
}
