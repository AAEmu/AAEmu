using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

using WorldIntegration = AAEmu.Game.WorldIntegration;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class KnockBack : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.KnockBack;

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
        if (caster is Character)
            Logger.Debug("Special effects: KnockBack value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4);

        if (target is not Unit trg || trg.Hp <= 0)
            return;

        // buffs.knockback_immune is the only shipped resist; everything else (stacking from a pack,
        // damping, mass) is the client's physics, exactly as retail.
        if (trg.Buffs.CheckKnockbackImmune())
        {
            Logger.Debug("KnockBack refused on {0}: knockback_immune", trg.ObjId);
            return;
        }

        var from = caster.Transform.World.Position;
        var to = trg.Transform.World.Position;
        if (!KnockBackRules.TryImpulse(from, to, value1, value2, out var impulse))
        {
            var (dirX, dirY) = KnockBackRules.DirectionFromYaw(caster.Transform.World.Rotation.Z);
            impulse = KnockBackRules.ImpulseAlong(dirX, dirY, value1, value2);
        }

        // SC = impulse for client physics. Server XY slide keeps combat/AOI honest without opening a
        // crowd-control CSMoveUnit bypass (a grace window was rejected as a movement cheat path).
        trg.BroadcastPacket(new SCKnockBackUnitPacket(trg.ObjId, impulse.X, impulse.Y, impulse.Z), true);

        if (trg is Character character)
        {
            ApplyServerAuthoritySlide(trg, impulse, value1);
            Logger.Debug("KnockBack player={0} horizontal={1} vertical={2} impulse=({3:F1},{4:F1},{5:F1})",
                character.Name, value1, value2, impulse.X, impulse.Y, impulse.Z);
            return;
        }

        // Zone-owned NPCs: dedicate physics applies the same impulse.
        if (WorldIntegration.ZoneAuthority)
        {
            WorldIntegration.RelayKnockBackToZone?.Invoke(trg.ObjId, impulse.X, impulse.Y, impulse.Z);
            return;
        }

        // Standalone World: no dedicate and clients cannot move NPCs — same server slide.
        ApplyServerAuthoritySlide(trg, impulse, value1);
    }

    private static void ApplyServerAuthoritySlide(Unit trg, System.Numerics.Vector3 impulse, int horizontalMagnitude)
    {
        if (!KnockBackRules.TryServerAuthoritySlide(impulse, horizontalMagnitude, out var delta))
            return;

        var p = trg.Transform.World.Position;
        trg.SetPosition(p.X + delta.X, p.Y + delta.Y, p.Z,
            trg.Transform.World.Rotation.X,
            trg.Transform.World.Rotation.Y,
            trg.Transform.World.Rotation.Z);
    }
}
