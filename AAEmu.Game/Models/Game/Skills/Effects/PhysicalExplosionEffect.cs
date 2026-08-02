using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

/// <summary>
/// physical_explosion_effects — a CryEngine physics blast: a pressure impulse over a radius, optionally
/// punching a hole of HoleSize in deformable geometry. 89 skills and 37 plot events carry one.
///
/// The local physics engine was removed in 39d1e80e, so the blast is raised through the impulse path that
/// ImpulseEffect already proved out: the zone through RelayImpulseToZone, which holds every simulated hull,
/// and the viewing clients through SCUnitImpulse. There is no separate WZ explosion opcode in the client's
/// </summary>
public class PhysicalExplosionEffect : EffectTemplate
{
    public float Radius { get; set; }
    public float HoleSize { get; set; }
    public float Pressure { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        if (target == null)
            return;

        // Radius is authored in millimetres like every other range in this data.
        var radius = Radius / 1000f;
        if (radius <= 0f)
            return;

        var blastCentre = target.Transform.World.Position;
        var caught = Core.Managers.World.WorldManager.GetAround<Units.Unit>(target, radius);

        foreach (var unit in caught)
        {
            // Push each body away from the centre, falling off linearly to nothing at the rim, scaled by the
            // authored pressure. The client and the zone each own a different class of body, so both are told,
            // exactly as ImpulseEffect does for an authored impulse.
            var delta = unit.Transform.World.Position - blastCentre;
            var distance = delta.Length();
            var direction = distance > 0.001f ? delta / distance : new System.Numerics.Vector3(0f, 0f, 1f);
            var falloff = Math.Clamp(1f - distance / radius, 0f, 1f);
            var force = Pressure * falloff;

            if (force <= 0f)
                continue;

            float[] vel = [direction.X * force, direction.Y * force, direction.Z * force];
            float[] angVel = [0f, 0f, 0f];
            float[] impulse = [vel[0], vel[1], vel[2]];
            float[] angImpulse = [0f, 0f, 0f];

            var impulseSource = new SkillCasterUnit(caster?.ObjId ?? unit.ObjId);

            WorldIntegration.RelayImpulseToZone?.Invoke(unit.ObjId, impulseSource, vel, angVel, impulse, angImpulse);
            unit.BroadcastPacket(new Core.Packets.G2C.SCUnitImpulsePacket(
                unit.ObjId, impulseSource,
                vel[0], vel[1], vel[2], angVel[0], angVel[1], angVel[2],
                impulse[0], impulse[1], impulse[2], angImpulse[0], angImpulse[1], angImpulse[2]), true);
        }

        Logger.Debug($"PhysicalExplosionEffect: radius {radius:0.0}m pressure {Pressure} caught {caught.Count} units");
    }
}
