using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSRestrictCheckPacket() : GamePacket(CSOffsets.CSRestrictCheckPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        // 10.0.2.13 (binary CSRestrictCheck 0x39c32000): restrictType i64 ("type") + restrictCode u8.
        var restrictType = stream.ReadInt64();
        var restrictCode = stream.ReadByte();
        // result 0 = not restricted -> the client proceeds with enter-world.
        Connection.SendPacket(new SCResultRestrictCheckPacket(restrictType, restrictCode, 0));
    }
}
