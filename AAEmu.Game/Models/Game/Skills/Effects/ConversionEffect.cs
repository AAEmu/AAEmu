using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class ConversionEffect : EffectTemplate
{
    public uint CategoryId { get; set; }
    public uint SourceCategoryId { get; set; }
    public int SourceValue { get; set; }
    public uint TargetCategoryId { get; set; }
    public int TargetValue { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        if (target is not Unit unit)
            return;

        // enum_conversion_categories: 1 mp_to_hp, 2 hp_to_mp.
        var fromMana = CategoryId == 1;
        var sourceMax = fromMana ? unit.MaxMp : unit.MaxHp;
        var sourceCurrent = fromMana ? unit.Mp : unit.Hp;
        var targetMax = fromMana ? unit.MaxHp : unit.MaxMp;
        var targetCurrent = fromMana ? unit.Hp : unit.Mp;

        // enum_conversion_source_categories: 1 absolute, 2 relative_to_max, 3 relative_to_current.
        var taken = SourceCategoryId switch
        {
            2 => (int)(sourceMax * (SourceValue / 100f)),
            3 => (int)(sourceCurrent * (SourceValue / 100f)),
            _ => SourceValue
        };

        // Never drain past what the unit actually has; a conversion must not push a pool negative.
        taken = Math.Clamp(taken, 0, sourceCurrent);
        if (taken <= 0)
            return;

        // enum_conversion_target_categories adds 4 relative_to_source — the common case, where the amount
        // handed over is a percentage of what was just taken.
        var given = TargetCategoryId switch
        {
            2 => (int)(targetMax * (TargetValue / 100f)),
            3 => (int)(targetCurrent * (TargetValue / 100f)),
            4 => (int)(taken * (TargetValue / 100f)),
            _ => TargetValue
        };

        given = Math.Max(0, given);

        if (fromMana)
        {
            unit.Mp = Math.Max(0, unit.Mp - taken);
            unit.Hp = Math.Min(unit.MaxHp, unit.Hp + given);
        }
        else
        {
            unit.Hp = Math.Max(0, unit.Hp - taken);
            unit.Mp = Math.Min(unit.MaxMp, unit.Mp + given);
        }

        Logger.Debug($"ConversionEffect: {(fromMana ? "mp->hp" : "hp->mp")} on {unit.ObjId}, took {taken} gave {given}");

        unit.BroadcastPacket(new Core.Packets.G2C.SCUnitPointsPacket(unit.ObjId, unit.Hp, unit.Mp), true);
    }
}
