using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSListMailPacket() : GamePacket(CSOffsets.CSListMailPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        // Empty struct
        Connection.ActiveChar.Mails.OpenMailbox();
    }
}
