using System;
using System.Numerics;

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

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        Logger.Debug("ImpulseEffect");

        var vel = new Vector3(VelImpulseX, VelImpulseY, VelImpulseZ);
        var angVel = new Vector3(AngvelImpulseX, AngvelImpulseY, AngvelImpulseZ);
        var impulse = new Vector3(ImpulseX, ImpulseY, ImpulseZ);
        var angImpulse = new Vector3(AngImpulseX, AngImpulseY, AngImpulseZ);

        caster.BroadcastPacket(new SCImpulseUnitPacket(caster.ObjId, casterObj, vel, angVel, impulse, angImpulse), true);

    }
}
