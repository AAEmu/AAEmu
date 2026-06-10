using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
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
        Logger.Trace("ImpulseEffect");

        var impulseTarget = ResolveImpulseTarget(caster, target);
        if (impulseTarget == null || impulseTarget is Unit { IsDead: true })
            return;

        var packet = new SCUnitImpulsePacket(
            impulseTarget.ObjId,
            VelImpulseX,
            VelImpulseY,
            VelImpulseZ,
            AngvelImpulseX,
            AngvelImpulseY,
            AngvelImpulseZ,
            ImpulseX,
            ImpulseY,
            ImpulseZ,
            AngImpulseX,
            AngImpulseY,
            AngImpulseZ);

        impulseTarget.BroadcastPacket(packet, impulseTarget is Character);
    }

    private static BaseUnit ResolveImpulseTarget(BaseUnit caster, BaseUnit target)
    {
        if (target is Slave)
            return target;

        if (caster is Slave)
            return caster;

        if (target == caster && caster is Character character)
        {
            var slave = GetCharacterSlave(character);
            if (slave != null)
                return slave;
        }

        return target;
    }

    private static Slave GetCharacterSlave(Character character)
    {
        if (character.ParentWorld == null)
            return null;

        return character.ParentWorld.SlaveManager.GetIsMounted(character.ObjId, out _)
               ?? character.ParentWorld.SlaveManager.GetActiveSlaveByOwnerObjId(character.ObjId);
    }
}
