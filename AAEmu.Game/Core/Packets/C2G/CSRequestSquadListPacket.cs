using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Squad;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSRequestSquadListPacket() : GamePacket(CSOffsets.CSRequestSquadListPacket, 1)
{
    public uint CatalogId { get; private set; }
    public int Page { get; private set; }

    public override void Read(PacketStream stream)
    {
        CatalogId = SquadFieldTypeWire.Read(stream).InstanceId;
        Page = stream.ReadInt32();
        SquadManager.Instance.RequestList(Connection.ActiveChar, CatalogId, Page);
    }
}
