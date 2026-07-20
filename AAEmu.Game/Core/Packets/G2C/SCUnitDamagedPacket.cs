using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnitDamagedPacket(
    CastAction castAction,
    SkillCaster skillCaster,
    uint casterId,
    uint targetId,
    int damage,
    int absorbed)
    : GamePacket(SCOffsets.SCUnitDamagedPacket, 5)
{
    public int _manaBurn;

    public byte HoldableId { get; set; }
    public SkillHitType HitType { get; set; }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(castAction);
        stream.Write(skillCaster);
        stream.WriteBc(casterId);
        stream.WriteBc(targetId);
        stream.Write((byte)0); // crimeState
        stream.WritePisc(damage, absorbed, 0);
        stream.WritePisc(0, 0, _manaBurn);
        stream.Write(HoldableId); // hol
        stream.Write((ushort)(288 | (ushort)HitType)); // de
        // stream.Write((ushort)623);
        stream.Write((byte)1); // flag
        stream.Write((byte)1); // result -> to debug info
        // TODO debug info
        return stream;
    }
}
