using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSExpeditionRenamePacket : GamePacket
{
    public CSExpeditionRenamePacket() : base(CSOffsets.CSExpeditionRenamePacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var id = stream.ReadUInt32(); // type(id)
        var name = stream.ReadString();
        var isExpedition = stream.ReadBoolean();

        Logger.Debug($"ExpeditionRename, Id: {id}, Name: {name}, IsExpedition: {isExpedition}");
    }
}
