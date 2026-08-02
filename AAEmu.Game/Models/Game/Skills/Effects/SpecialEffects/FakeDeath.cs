using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class FakeDeath : SpecialEffectAction
{
    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int value1,
        int value2,
        int value3,
        int value4)
    {
        if (target is not Unit affectedUnit)
            return;

        if (WorldIntegration.ZoneAuthority)
        {
            // Native WZFakeDeath contains only this bc. Descriptor values (including the one
            // live 700/70 tuple) do not participate in the Zone-authoritative transition.
            WorldIntegration.RelayFakeDeathToZone?.Invoke(affectedUnit.ObjId);
            return;
        }

        // table with reason 7. Standalone AAEmu has no reverse index for every Unit subtype, so
        // scan the world's NPC collection to produce the same state transition.
        foreach (var npc in affectedUnit.ParentWorld.GetAllNpcs())
        {
            if (!npc.AggroTable.ContainsKey(affectedUnit.ObjId))
                continue;

            npc.ClearAggroOfUnit(affectedUnit);
            if (npc.AggroTable.IsEmpty)
                npc.IsInBattle = false;
        }
    }
}
