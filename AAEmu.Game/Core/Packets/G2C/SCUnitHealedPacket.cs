using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnitHealedPacket(
    CastAction castAction,
    SkillCaster skillCaster,
    uint targetId,
    HealType healType,
    HealHitType healHitType,
    int value)
    : GamePacket(SCOffsets.SCUnitHealedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(castAction);
        stream.Write(skillCaster);
        stream.WriteBc(targetId);
        stream.Write((byte)healType); // h
        stream.Write((byte)healHitType); // h
        stream.Write(value); // a
        stream.Write(0); // o
        stream.Write((byte)1); // result -> to debug into
        return stream;
    }
}
