using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Buffs.Triggers;

public class AttackBuffTrigger(Buff owner, BuffTriggerTemplate template) : BuffTrigger(owner, template)
{
    public override void Execute(object sender, EventArgs eventArgs)
    {
        var args = eventArgs as OnAttackArgs;
        Logger.Trace("Buff[{0}] {1} executed. Applying {2}[{3}]!", _buff.Template.BuffId, this.GetType().Name, Template.Effect.GetType().Name, Template.Effect.Id);
        //Template.Effect.Apply()

        if (_owner is not Unit owner)
        {
            Logger.Warn("AttackTrigger owner is not a Unit");
            return;
        }

        // 10.0.2.13: buff_triggers.effect_on_source removed; effect applies to the owner (former mock-false path).
        var target = owner;

        Template.Effect.Apply(owner, new SkillCasterUnit(_owner.ObjId), target, new SkillCastUnitTarget(target.ObjId), new CastBuff(_buff),
            new EffectSource(), // TODO : EffectSource Type trigger 
            null, DateTime.UtcNow);
    }
}
