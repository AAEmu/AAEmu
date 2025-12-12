using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSDismissExpeditionPacket() : GamePacket(CSOffsets.CSDismissExpeditionPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        Logger.Debug("DismissExpedition");
        // Empty struct
        ExpeditionManager.Disband(Connection.ActiveChar);
    }
}
