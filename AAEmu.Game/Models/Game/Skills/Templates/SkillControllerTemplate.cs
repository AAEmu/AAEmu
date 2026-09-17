using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.SkillControllers;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Templates;

public class SkillControllerTemplate : EffectTemplate
{
    public uint KindId { get; set; }
    public int[] Value { get; set; } = new int[15];

    public byte ActiveWeaponId { get; set; }
    public uint EndSkillId { get; set; } // 10.0.2.13: skill_controllers.end_skill_id present again
    public override bool OnActionTime { get; }

    /// <summary>
    /// Starts the controller this row describes on the caster. A skill names its controller in
    /// <c>skills.skill_controller_id</c> and <c>Skill.Cast</c> starts it there; this is the other way in —
    /// a plot effect of type SkillController, of which 10.0.2.13 has 2,010 rows and none of which did
    /// anything, and buff triggers of kind 28.
    /// </summary>
    /// <remarks>
    /// Who may be moved is decided by <see cref="SkillControllerRules.CanCreateController"/> exactly as it is
    /// on the cast path, so a plot cannot drive a unit its caster does not control. A kind with no controller
    /// on this server still creates nothing.
    /// </remarks>
    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj,
        EffectSource source, SkillObject skillObject, DateTime time, CompressedGamePackets packetBuilder = null)
    {
        Logger.Debug("SkillControllerTemplate {0} (kind {1})", Id, KindId);

        if (caster is not Unit owner)
            return;

        if (!SkillControllerRules.CanCreateController(caster, owner))
            return;

        var controller = SkillController.CreateSkillController(this, caster, target ?? caster);
        if (controller == null)
            return;

        if (owner.ActiveSkillController != null)
            owner.ActiveSkillController.End();

        owner.ActiveSkillController = controller;
        controller.Execute();
    }
}
