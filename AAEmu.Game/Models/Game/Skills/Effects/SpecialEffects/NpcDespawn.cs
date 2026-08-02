using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class NpcDespawn : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.NpcDespawn;

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
        // Every enabled NpcDespawn link in 10.0.2.13 resolves to the skill source and carries
        // zero in all seven value fields. The selected source is therefore the only operand.
        if (caster is not Npc npc)
            return;

        if (WorldIntegration.ZoneAuthority)
        {
            // Zone owns the NPC lifecycle. Keep the World mirror and bc reserved until
            // ZWRemoveNpc confirms that WZNpcStartDespawn retired the authoritative unit.
            WorldIntegration.DeleteNpcMirror(npc, notifyZone: true);
        }
        else if (npc.Spawner != null)
        {
            npc.Spawner.Despawn(npc);
        }
        else
        {
            WorldIntegration.DeleteNpcMirror(npc, notifyZone: false);
        }
    }
}
