using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// </summary>
public class SCCountTotalMailPacket(CountUnreadMail count) : GamePacket(SCOffsets.SCCountTotalMailPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(count);
        return stream;
    }
}
