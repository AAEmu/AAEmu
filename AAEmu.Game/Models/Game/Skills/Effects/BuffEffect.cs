using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class BuffEffect : EffectTemplate
{
    public int Chance { get; set; }
    public int Stack { get; set; }
    public int AbLevel { get; set; }
    public BuffTemplate Buff { get; set; }

    /// <summary>
    /// The buff this effect applies, or 0 when <c>buff_effects.buff_id</c> names no <c>buffs</c> row.
    /// Callers test this against a buff id they already hold, so 0 never matches a real buff.
    /// </summary>
    public override uint BuffId => BuffEffectDispatchRules.ResolveBuffId(Buff);
    public override bool OnActionTime => BuffEffectDispatchRules.HasTick(Buff);

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        // 41 buff_effects rows in 10.0.2.13 name a buff_id that has no buffs row. The row is still
        // reachable — this skill path, the tick path, buff triggers and plot effects all resolve it
        // through the effect-id table — so it has to do nothing instead of dereferencing a null Buff.
        if (!BuffEffectDispatchRules.IsDispatchable(Buff))
            return;

        if (target is Unit trg)
        {
            var hitType = SkillHitType.Invalid;
            if ((source.Skill?.HitTypes.TryGetValue(trg.ObjId, out hitType) ?? false)
                && (source.Skill?.SkillMissed(trg.ObjId) ?? false))
            {
                return;
            }
        }

        if (caster != null)
        {
            if (Random.Shared.Next(0, 101) > Chance)
            {
                caster.ConditionChance = false;
                return;
            }
            else
            {
                caster.ConditionChance = true;
            }
        }

        if (Buff.RequireBuffId > 0 && !target.Buffs.CheckBuff(Buff.RequireBuffId))
            return; // TODO send error?
        // tagged_require_buffs is the tag form of the same prerequisite: the target must already carry
        // the tag, e.g. 4627 가벼운 발걸음 needs tag 831 무겁다.
        if (target.Buffs.GetMissingRequiredBuffTag(Buff) > 0)
            return; // TODO send error?
        if (target.Buffs.CheckBuffImmune(Buff, caster, source.Skill))
        {
            target.Buffs.BroadcastBuffImmune(caster, castObj, casterObj);
            return;
        }

        uint abLevel = 1;
        if (caster is Character character)
        {
            // No log line here: this runs once per buff application, and Skill.Use already logs the cast
            // ("Created SkillTlId …") with the skill the buff came from.
            if (source.Skill != null)
            {
                var template = source.Skill.Template;
                var abilityLevel = character.GetAbLevel(source.Skill.Template.AbilityId);
                if (template.LevelStep != 0)
                    abLevel = (uint)(abilityLevel / template.LevelStep * template.LevelStep);
                else
                    abLevel = (uint)template.AbilityLevel;

                //Dont allow lower than minimum ablevel for skill or infinite debuffs can happen
                abLevel = (uint)Math.Max(template.AbilityLevel, (int)abLevel);
            }
            else if (source.Buff != null)
            {
                //not sure?
            }
        }
        else
        {
            if (source.Skill != null)
            {
                abLevel = (uint)source.Skill.Template.AbilityLevel;
            }
        }

        // TODO Doesn't let the quest work Id=2488 "A Mother's Tale", 13, "Lilyut Hills", "Nuian Main"
        // Safeguard to prevent accidental flagging
        //if (Buff.Kind == BuffKind.Bad && !caster.CanAttack(target) && caster != target)
        //    return;

        SailFoldBuffs.ApplyAnimExclusivity(target, Buff.Id);

        target.Buffs.AddBuff(new Buff(target, caster, casterObj, Buff, source.Skill, time) { AbLevel = abLevel });

        // Check if a bad buff was applied to a friendly player (bloodlust / Felon).
        // Sail fold "debuffs" are Kind=debuff but target the hull Slave — that is not PvP.
        var relationToTarget = caster?.GetRelationStateTo(target) ?? RelationState.Neutral;
        if (Buff.Kind == BuffKind.Bad && target is Character &&
            relationToTarget == RelationState.Friendly && caster != target &&
            !target.Buffs.CheckBuff((uint)BuffConstants.Retribution))
        {
            (caster as Unit)?.SetCriminalState(true, target);
        }
    }
}
