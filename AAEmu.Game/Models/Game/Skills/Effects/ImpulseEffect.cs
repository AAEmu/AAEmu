using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class ImpulseEffect : EffectTemplate
{
    public float VelImpulseX { get; set; }
    public float VelImpulseY { get; set; }
    public float VelImpulseZ { get; set; }
    public float AngvelImpulseX { get; set; }
    public float AngvelImpulseY { get; set; }
    public float AngvelImpulseZ { get; set; }
    public float ImpulseX { get; set; }
    public float ImpulseY { get; set; }
    public float ImpulseZ { get; set; }
    public float AngImpulseX { get; set; }
    public float AngImpulseY { get; set; }
    public float AngImpulseZ { get; set; }

    public override bool OnActionTime => false;

    /// <summary>
    /// Applies the authored impulse to the target. The vehicle stunts — flip, wheelie, spinning
    /// jump — are all this effect, so while it did nothing a skill played its sound and particles
    /// client-side and the vehicle never moved.
    /// </summary>
    /// <remarks>
    /// impulse_effects authors its vectors in the source's own frame and the receiver orients them,
    /// rotates all four by a quaternion at LAB_39269CEB before handing them to the physics apply:
    /// the unit's own orientation when the caster is the unit itself, otherwise a yaw built from
    /// atan2f over the vector between source and unit, which is what turns a knockback away from
    /// whoever threw it. Rotating server-side as well would apply that twice.
    ///
    /// impulse source for types 0, 1 and 4 only and returns 0 for anything else, and a skill cast
    /// from a vehicle seat arrives as a mount caster (type 3) — which the client drops with
    /// "invalid skill source. type(3)" after parsing the packet cleanly.
    ///
    /// Two consumers need the result, because each owns a different class of body: the zone
    /// clients through SCUnitImpulse — the only path that moves a wheeled vehicle, since the
    /// driving client integrates it.
    /// </remarks>
    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        if (target is not Unit targetUnit)
            return;

        float[] vel = [VelImpulseX, VelImpulseY, VelImpulseZ];
        float[] angVel = [AngvelImpulseX, AngvelImpulseY, AngvelImpulseZ];
        float[] impulse = [ImpulseX, ImpulseY, ImpulseZ];
        float[] angImpulse = [AngImpulseX, AngImpulseY, AngImpulseZ];

        // Caster and target coincide for a self-impulse — every vehicle stunt — which is the branch
        // that orients by the unit's own rotation instead of by a direction away from a source.
        var impulseSource = new SkillCasterUnit(caster?.ObjId ?? targetUnit.ObjId);

        WorldIntegration.RelayImpulseToZone?.Invoke(targetUnit.ObjId, impulseSource, vel, angVel, impulse, angImpulse);

        targetUnit.BroadcastPacket(
            new SCUnitImpulsePacket(
                targetUnit.ObjId, impulseSource,
                vel[0], vel[1], vel[2],
                angVel[0], angVel[1], angVel[2],
                impulse[0], impulse[1], impulse[2],
                angImpulse[0], angImpulse[1], angImpulse[2]),
            true);

        Logger.Debug("ImpulseEffect on {0} from {1}: vel=({2:0.0},{3:0.0},{4:0.0}) angVel=({5:0.00},{6:0.00},{7:0.00})",
            targetUnit.ObjId, impulseSource.ObjId, vel[0], vel[1], vel[2], angVel[0], angVel[1], angVel[2]);
    }
}
