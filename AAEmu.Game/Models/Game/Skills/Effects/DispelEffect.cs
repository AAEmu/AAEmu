using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class DispelEffect : EffectTemplate
{
    public int DispelCount { get; set; }
    public int CureCount { get; set; }
    public uint BuffTagId { get; set; }
    /// <summary><c>dispel_effects.stack</c> — see <see cref="DispelRules"/>.</summary>
    public int Stack { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        Logger.Trace("DispelEffect {0}", Id);

        if (BuffTagId > 0 && !target.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId(BuffTagId)))
            return;

        var count = DispelRules.StackCount(Stack, DispelCount, CureCount);

        if (BuffTagId > 0)
        {
            // Tag remove is split by the same Good/Bad rule the untagged path uses rather than removing
            // max(dispel, cure) applications of whichever kind happened to be there: a cure-only row used to
            // strip the victim's good buffs. Sail fold state is a "debuff" on a friendly hull, and CureCount
            // is what those rows author, so it still cures.
            var kind = DispelRules.TargetsGoodBuffs(caster.CanAttack(target), DispelCount, CureCount)
                ? BuffKind.Good
                : BuffKind.Bad;
            target.Buffs.RemoveBuffs(kind, count, BuffTagId);
            SailFoldBuffs.OnFoldStateDispelled(caster, BuffTagId);
            return;
        }

        if (DispelCount > 0 && caster.CanAttack(target))
            target.Buffs.RemoveBuffs(BuffKind.Good, DispelCount);
        if (CureCount > 0 && !caster.CanAttack(target))
            target.Buffs.RemoveBuffs(BuffKind.Bad, CureCount);
    }
}
