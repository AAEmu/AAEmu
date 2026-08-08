using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>Publishes Marketplace cash balances and refresh metadata.</summary>
public class SCICSCashPointPacket(int point, int bonusPoint = 0, bool reload = false, byte noticeType = 0)
    : GamePacket(SCOffsets.SCICSCashPointPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(point);
        stream.Write(bonusPoint);
        stream.Write(reload);
        stream.Write(noticeType);
        return stream;
    }
}
