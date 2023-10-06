using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSChangeExpeditionOwnerPacket : GamePacket
{
    public CSChangeExpeditionOwnerPacket() : base(CSOffsets.CSChangeExpeditionOwnerPacket, 1)
    {
    }

    public override void Read(PacketStream stream)
    {
        var id = stream.ReadUInt32(); // type(id)

        Logger.Debug("ChangeExpeditionOwner, Id: {0}", id);
        ExpeditionManager.ChangeOwner(Connection, id);
    }
}
