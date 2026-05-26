using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.SkillControllers;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Templates;

public class SkillControllerTemplate : EffectTemplate
{
    public uint KindId { get; set; }
    public int[] Value { get; set; } = new int[15];

    public byte ActiveWeaponId { get; set; }
    // TODO 1.2 // public uint EndSkillId { get; set; }
    public override bool OnActionTime { get; }

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj,
        EffectSource source, SkillObject skillObject, DateTime time, CompressedGamePackets packetBuilder = null)
    {
        // caster = the unit to be moved (e.g. the NPC being pulled / leapt at);
        // target = the reference point (e.g. the player pulling). Naming is
        // inverted from the buff path because here "caster" is whoever the
        // skill applies the effect TO, not whoever cast the parent skill.
        Logger.Debug("SkillControllerTemplate.Apply: sc_id={0} kind={1} caster={2} target={3}",
            Id, KindId, caster?.ObjId, target?.ObjId);

        if (caster is not Unit ownerUnit)
            return;

        // Skip server-side SC for Characters — the 1.2 client moves itself from
        // the buff / SC packets and a server-driven SC on top causes double
        // displacement.
        if (caster is Character)
            return;

        // Pull / Leap / Dash constructors deref target.Transform.World.Position
        // immediately. Without a reference target (area aura, spawn effect, or
        // target disconnected between cast and apply) those three SC kinds would
        // NRE on construction. Wandering does not need a target but bailing
        // here for the missing target is the safer default — a target-less
        // displacement has no defined direction anyway.
        if (target == null)
        {
            Logger.Warn("SkillControllerTemplate.Apply: target is null for sc_id={0} kind={1}, skipping", Id, KindId);
            return;
        }

        var sc = SkillController.CreateSkillController(this, caster, target);
#pragma warning disable CA1508 // Factory can return null for unimplemented controller kinds
        if (sc is not null)
#pragma warning restore CA1508
        {
            if (ownerUnit.ActiveSkillController != null)
                ownerUnit.ActiveSkillController.End(force: true);
            ownerUnit.ActiveSkillController = sc;
            sc.Execute();
        }
        else
        {
            Logger.Warn("SkillControllerTemplate.Apply: factory returned null for sc_id={0} kind={1} — controller class not implemented", Id, KindId);
        }
    }
}
