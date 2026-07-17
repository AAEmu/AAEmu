using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCKickedPacket(KickedReason reason, string msg) : GamePacket(SCOffsets.SCKickedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)reason);
        stream.Write(msg);
        return stream;
    }
}

public enum KickedReason : byte
{
    KickDuplicateAccount = 0x0,
    KickByGm = 0x1,
    KickByMaintenance = 0x2,
    KickByInvalidDoodadInteraction = 0x3,
}
