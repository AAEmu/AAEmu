using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Skills;
using GameMate = AAEmu.Game.Models.Game.Units.Mate;

namespace AAEmu.Game.Models.Tasks.Mate;

/// <summary>
/// Server-paced melee for battle mates after <c>CSChangeMateTarget</c> / aggressive mode.
/// Character <see cref="UseAutoAttackSkillTask"/> is Character-only; mates use
/// <c>npcs.base_skill_id</c> (skill 2 for the brown wolfhound) via <see cref="SkillCasterUnit"/>.
/// </summary>
public class UseMateAutoAttackSkillTask : SkillTask
{
    private readonly GameMate _mate;
    private readonly SkillTemplate _skillTemplate;

    public UseMateAutoAttackSkillTask(Skill skill, GameMate mate) : base(skill)
    {
        _skillTemplate = skill.Template;
        _mate = mate;
        Cancelled = false;
    }

    public override void Execute()
    {
        var target = _mate.CurrentTarget as Unit;

        if (_mate.Hp <= 0 || target == null || target.Hp <= 0 || target.ObjId == _mate.ObjId || Cancelled)
        {
            StopOrderedAttack();
            return;
        }

        if (_mate.SkillTask != null)
            return;
        if (_mate.GlobalCooldown >= DateTime.UtcNow)
            return;

        if (!_mate.CanAttack(target))
            return;

        // Skill.Use range for skill 2 uses weapon_slot_for_range_id (Mainhand=15) with fist
        // default max 3 when empty — not skills.max_range (25). Chasing to template MaxRange
        // left the pet at 5–9 m and every swing TooFarRange.
        var maxRange = GetEffectiveMaxRange();
        var distance = _mate.GetDistanceTo(target, true);
        if (distance > maxRange)
        {
            var stepSeconds = RepeatInterval.TotalSeconds > 0 ? (float)RepeatInterval.TotalSeconds : 0.2f;
            var step = _mate.BaseMoveSpeed * stepSeconds;
            _mate.MoveTowards(target.Transform.World.Position, step, 4, maxRange * 0.85f);
            return;
        }

        // Fresh Skill each swing — a TooFarRange Use releases TlId on the prior instance.
        var skill = new Skill(_skillTemplate);
        var casterCaster = new SkillCasterUnit(_mate.ObjId);
        var targetCaster = new SkillCastUnitTarget(target.ObjId);
        var skillObject = SkillObject.GetByType(SkillObjectType.None);
        var result = skill.Use(_mate, casterCaster, targetCaster, skillObject, true, out _);
        if (result == SkillResult.TooFarRange || result == SkillResult.TooCloseRange)
        {
            var stepSeconds = RepeatInterval.TotalSeconds > 0 ? (float)RepeatInterval.TotalSeconds : 0.2f;
            _mate.MoveTowards(target.Transform.World.Position, _mate.BaseMoveSpeed * stepSeconds, 4,
                maxRange * 0.85f);
            return;
        }

        if (result == SkillResult.Success)
            _mate.LastCombatActivity = DateTime.UtcNow;

        var newDelay = TimeSpan.FromMilliseconds(SkillManager.GetAttackDelay(_skillTemplate, _mate));
        if (newDelay != RepeatInterval)
            RepeatInterval = newDelay;
    }

    /// <summary>
    /// Same effective max as <see cref="Skill.Use"/> when <c>WeaponSlotForRangeId</c> is set:
    /// equipped weapon holdable range, else fist default 3 (skill 2 Mainhand slot empty).
    /// </summary>
    private float GetEffectiveMaxRange()
    {
        if (_skillTemplate.WeaponSlotForRangeId > 0)
        {
            if (_mate.Equipment?.GetItemBySlot(_skillTemplate.WeaponSlotForRangeId)?.Template
                is WeaponTemplate weapon && weapon.HoldableTemplate != null && weapon.HoldableTemplate.MaxRange > 0)
                return weapon.HoldableTemplate.MaxRange;
            return 3f;
        }

        return _skillTemplate.MaxRange > 0 ? _skillTemplate.MaxRange : 3f;
    }

    private void StopOrderedAttack()
    {
        var skillId = _skillTemplate?.Id ?? 0;
        Cancelled = true;
        _mate.IsAutoAttack = false;
        _mate.AutoAttackTask = null;
        Cancel();
        if (skillId != 0)
            _mate.BroadcastPacket(new SCSkillStoppedPacket(_mate.ObjId, skillId), true);
    }
}
