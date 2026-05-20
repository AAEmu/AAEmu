using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.Skills;

public class UseAutoAttackSkillTask : SkillTask
{
    private readonly Skill _skill;
    private readonly Character _caster;

    public UseAutoAttackSkillTask(Skill skill, Character caster) : base(skill)
    {
        _skill = skill;
        _caster = caster;
        Cancelled = false;
    }

    public override void Execute()
    {
        var target = _caster.CurrentTarget as Unit;

        // Stop conditions: dead, no target, target dead, self-target, cancelled
        if (_caster.Hp <= 0 || target == null || target.Hp <= 0 || target.ObjId == _caster.ObjId || Cancelled)
        {
            StopAutoAttack();
            return;
        }

        // Skill-pause: while another skill is casting or during GCD, skip this tick.
        // We don't cancel — auto-attack will resume on the next tick after skill ends.
        if (_caster.SkillTask != null)
            return;
        if (_caster.GlobalCooldown >= DateTime.UtcNow)
            return;

        if (!_caster.CanAttack(target))
            return;

        // Range check using weapon max range or skill max range — pause (don't cancel)
        // if target moved out of range, so player walking back into range resumes attacks.
        var maxRange = GetEffectiveMaxRange();
        var distance = _caster.GetDistanceTo(target);
        if (distance > maxRange)
            return;

        var casterCaster = new SkillCasterUnit(_caster.ObjId);
        var targetCaster = new SkillCastUnitTarget(target.ObjId);
        var skillObject = SkillObject.GetByType(SkillObjectType.None);

        _skill.Use(_caster, casterCaster, targetCaster, skillObject, true, out _);

        // Dynamically adjust delay if attack speed changed (buff/debuff)
        var newDelay = TimeSpan.FromMilliseconds(SkillManager.GetAttackDelay(_skill.Template, _caster));
        if (newDelay != RepeatInterval)
            RepeatInterval = newDelay;
    }

    private void StopAutoAttack()
    {
        Cancelled = true;
        _caster.IsAutoAttack = false;
        _caster.AutoAttackTask = null;
        Cancel();
    }

    /// <summary>Get max attack range from equipped weapon or fall back to skill template.</summary>
    private float GetEffectiveMaxRange()
    {
        EquipmentItemSlot slot = _skill.Template.Id switch
        {
            2 => EquipmentItemSlot.Mainhand,
            3 => EquipmentItemSlot.Offhand,
            4 => EquipmentItemSlot.Ranged,
            _ => EquipmentItemSlot.Mainhand
        };

        var weapon = _caster.Equipment?.GetItemBySlot((int)slot);
        if (weapon?.Template is WeaponTemplate wt && wt.HoldableTemplate != null && wt.HoldableTemplate.MaxRange > 0)
            return wt.HoldableTemplate.MaxRange;

        // Fallback to skill template max range
        return _skill.Template.MaxRange > 0 ? _skill.Template.MaxRange : 4f;
    }
}
