using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

using WorldIntegration = AAEmu.Game.WorldIntegration;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class RestoreManaEffect : EffectTemplate
{
    public bool UseFixedValue { get; set; }
    public int FixedMin { get; set; }
    public int FixedMax { get; set; }
    public bool UseLevelValue { get; set; }
    public float LevelMd { get; set; }
    public int LevelVaStart { get; set; }
    public int LevelVaEnd { get; set; }
    public bool Percent { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        Logger.Trace("RestoreManaEffect");

        if (target is not Unit)
            return;
        var trg = (Unit)target;
        var min = 0;
        var max = 0;
        if (UseFixedValue)
        {
            min += FixedMin;
            max += FixedMax;
        }

        var unk = 0f;
        var unk2 = 1f;
        var skillLevel = 1;
        if (source != null && source.Skill != null)
        {
            skillLevel = (source.Skill.Level - 1) * source.Skill.Template.LevelStep + source.Skill.Template.AbilityLevel;
            if (skillLevel >= source.Skill.Template.AbilityLevel)
                unk = 0.15f * (skillLevel - source.Skill.Template.AbilityLevel + 1);
            unk2 = (1 + unk) * 1.3f;
        }

        if (UseLevelValue)
        {
            var levelMd = (unk + 1) * LevelMd;
            min += (int)(((Unit)caster).LevelDps * levelMd + 0.5f);
            max += (int)((((skillLevel - 1) * 0.020408163f * (LevelVaEnd - LevelVaStart) + LevelVaStart) * 0.0099999998f + 1f) *
                         ((Unit)caster).LevelDps * levelMd + 0.5f);
        }

        // TODO: the MDps term stays out. MDps/MDpsInc (Unit.cs, spell_dps / spell_dps_inc) now exist, and
        // the composition it belongs in is the caster's, but the factor is unresolved: the surrounding
        // expression carries the level-derived `unk2` here, and `unk` is 0.15 per ability level over the
        // ability's own level, which is a level curve rather than a spell-power coefficient. The 16 shipped
        // rows that would exercise it author level_md 0 (restore-mana effects 9, 21, 32, 36, 37, 39, 41,
        // 49, 61 …), so no shipped row's number moves either way until the factor is known.
        // min += (int)((caster.MDps + caster.MDpsInc) * 0.001f * unk2 + 0.5f);
        // max += (int)((caster.MDps + caster.MDpsInc) * 0.001f * unk2 + 0.5f);

        var value = RestoreManaEffectRules.IsPercent(Percent, UseFixedValue)
            // percent (144 of 261 rows): a share of the restored unit's maximum mana instead of the
            // absolute composition above, for the same reason HealEffect's percent branch replaces its
            // own — the rows author round shares and read as single-digit absolutes.
            ? RestoreManaEffectRules.PercentAmount(trg.MaxMp, FixedMin, FixedMax, Random.Shared.Next(0, 101))
            : Random.Shared.Next(min, max);

        trg.BroadcastPacket(new SCUnitHealedPacket(castObj, casterObj, trg.ObjId, HealType.Mana, HealHitType.HealHit, value), true);
        trg.Mp += value;
        trg.Mp = Math.Min(trg.Mp, trg.MaxMp);
        trg.BroadcastPacket(new SCUnitPointsPacket(trg.ObjId, trg.Hp, trg.Mp), true);

        if (WorldIntegration.ZoneAuthority && packetBuilder == null)
        {
            var inCharge = caster is Unit u && u.ObjId != 0 ? u.ObjId : trg.ObjId;
            WorldIntegration.RelayUnitHealedToZone?.Invoke(
                castObj, casterObj, trg.ObjId, HealType.Mana, HealHitType.HealHit, value, inCharge, false);
            WorldIntegration.RelayUnitPointsToZone?.Invoke(trg.ObjId, trg.Hp, trg.Mp);
        }
    }
}
