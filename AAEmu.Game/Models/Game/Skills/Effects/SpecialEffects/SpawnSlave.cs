using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class SpawnSlave : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.SpawnSlave;

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
        // Effects run at cast-end (after SCSkillFired). A cancelled cast must never create a hull —
        // that is what produced overlapping yawls when StopCasting was ignored under ZoneAuthority.
        if (skill is { Cancelled: true })
        {
            Logger.Info("SpawnSlave skipped: skill cancelled tl={0} id={1}", skill.TlId, skill.Id);
            return;
        }

        if (caster is not Character owner)
            return;

        if (casterObj is not SkillItem skillData)
        {
            Logger.Warn("SpawnSlave: caster is not SkillItem for {0}", owner.Name);
            return;
        }

        Logger.Debug(
            "SpawnSlave char={0} item={1} tpl={2} skill={3}",
            owner.Name, skillData.ItemId, skillData.ItemTemplateId, skill?.Id ?? 0);

        owner.ParentWorld.SlaveManager.Create(owner, skillData);
    }
}
