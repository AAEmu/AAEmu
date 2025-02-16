using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSendExpeditionImmigrationListPacket : GamePacket
{
    public CSSendExpeditionImmigrationListPacket() : base(CSOffsets.CSSendExpeditionImmigrationListPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        Logger.Debug("CSSendExpeditionImmigrationListPacket");
    }
}
