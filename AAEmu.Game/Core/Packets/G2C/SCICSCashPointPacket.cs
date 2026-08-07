using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCICSCashPointPacket(int point, int bonusPoint = 0, bool reload = false, byte noticeType = 0)
    : GamePacket(SCOffsets.SCICSCashPointPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(point);        // i32 point
        stream.Write(bonusPoint);   // i32 bpoint
        stream.Write(reload);       // bool reload
        stream.Write(noticeType);   // u8 noticeType
        return stream;
    }
}
