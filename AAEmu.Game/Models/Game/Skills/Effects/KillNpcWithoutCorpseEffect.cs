using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class KillNpcWithoutCorpseEffect : EffectTemplate
{
    public uint NpcId { get; set; }
    public float Radius { get; set; }
    public bool GiveExp { get; set; }
    public bool Vanish { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        Logger.Info($"KillNpcWithoutCorpseEffect npc={NpcId}, Radius={Radius}, GiveExp={GiveExp}, Vanish={Vanish}");

        if (caster is Character) { return; } // does not apply to the character
        if (Vanish)
        {
            // Fixed: "Trainer Daru" disappears after selling a bear
            // quest 3449, buff=4112
            RemoveEffectsAndDelete((Unit)caster, ExperiencedBy(caster, target));
        }
        else
        {
            var npcs = WorldManager.GetAround<Npc>(target, Radius);
            if (caster is Npc thisNpc)
                npcs.Add(thisNpc);
            if (npcs == null) { return; }
            // The unit the exp is credited to is the one the removed npcs are resolved against, and it is the
            // same for all of them: the effect runs on one plot step.
            var killer = ExperiencedBy(caster, target);
            foreach (var npc in npcs.Where(npc => npc.TemplateId == NpcId))
            {
                RemoveEffectsAndDelete(npc, killer);
            }
        }
    }

    /// <summary>
    /// The character credited when <c>give_exp</c> is set: whoever the removed npc was fighting, falling back
    /// to the unit the effect was applied to.
    /// </summary>
    /// <remarks>
    /// <c>kill_npc_without_corpse_effects.give_exp</c> is set on 17 of the 1,849 rows (effects 437, 723, 1237-1244,
    /// 1442-1443, 1606, 2020, 2172, 2950) and was not loaded. The effect runs with the npc as its caster — the
    /// skill pipeline resolves the template id to an npc and applies it with that npc, or with the target when
    /// there is none — so the player is not the caster here.
    /// </remarks>
    private static Character ExperiencedBy(BaseUnit caster, BaseUnit target)
    {
        var npc = caster as Npc ?? target as Npc;
        return npc?.CurrentAggroTarget as Character
               ?? caster?.GetOwnerCharacter()
               ?? target?.GetOwnerCharacter();
    }

    private void RemoveEffectsAndDelete(Unit unit, Character experiencedBy)
    {
        unit.Buffs.RemoveAllEffects();
        if (unit is not Npc npc)
            return;

        // give_exp (17 rows): the npc dies for the quest without a corpse, and the player who killed it is
        // still owed its kill exp. Npc.DoDie is not on this path at all — the npc is despawned, not killed —
        // so the award is taken here, once, from the same KillExp the ordinary death branch uses.
        if (GiveExp)
            GrantKillExp(npc, experiencedBy);

        if (WorldIntegration.ZoneAuthority)
        {
            WorldIntegration.DeleteNpcMirror(npc, true);
        }
        else if (npc.Spawner != null)
        {
            npc.DoDespawn(npc);
            npc.Spawner.DespawnWithRespawn(npc);
        }
    }

    private static void GrantKillExp(Npc npc, Character experiencedBy)
    {
        if (experiencedBy == null || npc.KillExp <= 0)
            return;

        experiencedBy.AddExp(npc.KillExp, true);
        Logger.Debug("KillNpcWithoutCorpseEffect: {0} gained {1} exp for npc {2}",
            experiencedBy.Name, npc.KillExp, npc.TemplateId);
    }
}
