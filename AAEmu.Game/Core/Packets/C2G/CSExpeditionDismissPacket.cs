using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSExpeditionDismissPacket : GamePacket
{
    public CSExpeditionDismissPacket() : base(CSOffsets.CSExpeditionDismissPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        Logger.Debug("ExpeditionDismiss");
        // Empty struct
        ExpeditionManager.Disband(Connection.ActiveChar);
    }
}
