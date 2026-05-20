using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.Skills;

public class UseAutoAttackSkillTask : SkillTask
{
    private readonly Character _caster;
    private readonly Skill _mainhandSkill;

    // Offhand skill — only set when dual-wielding (two 1H weapons in mainhand + offhand)
    private Skill _offhandSkill;

    public UseAutoAttackSkillTask(Skill skill, Character caster) : base(skill)
    {
        _mainhandSkill = skill;
        _caster = caster;
        Cancelled = false;

        DetectDualWield();
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

        // Range check — pause (don't cancel) if target moved out of range
        var maxRange = GetEffectiveMaxRange();
        var distance = _caster.GetDistanceTo(target);
        if (distance > maxRange)
            return;

        var casterCaster = new SkillCasterUnit(_caster.ObjId);
        var targetCaster = new SkillCastUnitTarget(target.ObjId);
        var skillObject = SkillObject.GetByType(SkillObjectType.None);

        // Fire mainhand attack
        _mainhandSkill.Use(_caster, casterCaster, targetCaster, skillObject, true, out _);

        // Dual-wield: fire offhand attack on the same swing
        if (_offhandSkill != null)
        {
            var offCaster = new SkillCasterUnit(_caster.ObjId);
            var offTarget = new SkillCastUnitTarget(target.ObjId);
            var offSkillObject = SkillObject.GetByType(SkillObjectType.None);
            _offhandSkill.Use(_caster, offCaster, offTarget, offSkillObject, true, out _);
        }

        // Adjust delay if attack speed changed (buff/debuff/weapon swap)
        var newDelay = TimeSpan.FromMilliseconds(SkillManager.GetAttackDelay(_mainhandSkill.Template, _caster));
        if (newDelay != RepeatInterval)
            RepeatInterval = newDelay;
    }

    /// <summary>
    /// If we wield a melee weapon mainhand AND another weapon (not shield) offhand,
    /// queue the offhand auto-attack skill (id 3) on the same swing as mainhand (id 2).
    /// </summary>
    private void DetectDualWield()
    {
        // Only for melee mainhand (skill 2)
        if (_mainhandSkill.Template.Id != 2)
            return;

        var offhandItem = _caster.Equipment?.GetItemBySlot((int)EquipmentItemSlot.Offhand);
        if (offhandItem?.Template is not WeaponTemplate)
            return; // Shield, empty, or non-weapon — not dual-wield

        var offhandTemplate = SkillManager.Instance.GetSkillTemplate(3);
        if (offhandTemplate == null)
            return;

        _offhandSkill = new Skill(offhandTemplate);
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
        EquipmentItemSlot slot = _mainhandSkill.Template.Id switch
        {
            4 => EquipmentItemSlot.Ranged,
            _ => EquipmentItemSlot.Mainhand
        };

        var weapon = _caster.Equipment?.GetItemBySlot((int)slot);
        if (weapon?.Template is WeaponTemplate wt && wt.HoldableTemplate != null && wt.HoldableTemplate.MaxRange > 0)
            return wt.HoldableTemplate.MaxRange;

        return _mainhandSkill.Template.MaxRange > 0 ? _mainhandSkill.Template.MaxRange : 4f;
    }
}
