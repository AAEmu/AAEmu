using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class AggroEffect : EffectTemplate
{
    public bool UseFixedAggro { get; set; }
    public int FixedMin { get; set; }
    public int FixedMax { get; set; }
    public bool UseLevelAggro { get; set; }
    public float LevelMd { get; set; }
    public int LevelVaStart { get; set; }
    public int LevelVaEnd { get; set; }
    public bool UseChargedBuff { get; set; }
    public uint ChargedBuffId { get; set; }
    public float ChargedMul { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        // 353 buff_triggers rows carry an AggroEffect and every one of them is cast by an Npc, a mate or a
        // slave. Requiring a Character caster rolled the value and threw it away for all of them; what the
        // effect needs is a spawned unit and a target that owns an aggro table.
        if (!AggroEffectRules.CanCast(caster))
            return;

        if (target is not Npc npc || !AggroEffectRules.CanHoldAggro(target))
            return;

        var casterUnit = (Unit)caster;

        Logger.Info($"AggroEffect: UseFixedAggro={UseFixedAggro}, UseLevelAggro={UseLevelAggro}, UseChargedBuff={UseChargedBuff}");

        var min = 0.0f;
        var max = 0.0f;

        if (UseLevelAggro)
        {
            var lvlMd = casterUnit.LevelDps * LevelMd;
            var levelModifier = (((source.Skill?.Level ?? 1) - 1) / 49 * (LevelVaEnd - LevelVaStart) + LevelVaStart) * 0.01f;

            min += lvlMd - levelModifier * lvlMd + 0.5f;
            max += (levelModifier + 1) * lvlMd + 0.5f;
        }

        if (UseChargedBuff)
        {
            var effect = caster.Buffs.GetEffectFromBuffId(ChargedBuffId);
            if (effect != null)
            {
                min += ChargedMul * effect.Charge;
                max += ChargedMul * effect.Charge;
                effect.Exit();
            }
        }

        if (UseFixedAggro)
        {
            min += FixedMin;
            max += FixedMax;
        }

        var value = (int)Random.Shared.Next(min, max);
        if (WorldIntegration.ZoneAuthority)
        {
            WorldIntegration.PublishAggro(npc, casterUnit, (uint)Math.Max(0, value), castObj);
            return;
        }

        npc.SendPacketToPlayers(
            [casterUnit, npc],
            new SCAiAggroPacket(npc.ObjId, AiAggroEntry.FromDirectValue(casterUnit.ObjId, value)));
        npc.AddUnitAggro(AggroKind.Etc, casterUnit, value);
    }
}
