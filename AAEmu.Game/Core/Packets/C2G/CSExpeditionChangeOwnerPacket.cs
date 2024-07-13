using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSExpeditionChangeOwnerPacket : GamePacket
{
    public CSExpeditionChangeOwnerPacket() : base(CSOffsets.CSExpeditionChangeOwnerPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var id = stream.ReadUInt32(); // type(id)

        Logger.Debug($"ExpeditionChangeOwner, Id: {id}");
        ExpeditionManager.ChangeOwner(Connection, id);
    }
}
