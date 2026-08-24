using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Squad;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSReadySquadPacket() : GamePacket(CSOffsets.CSReadySquadPacket, 1)
{
    public bool Ready { get; private set; }
    public uint CatalogId { get; private set; }

    public override void Read(PacketStream stream)
    {
        Ready = stream.ReadBoolean();
        CatalogId = SquadFieldTypeWire.Read(stream).InstanceId;
        SquadManager.Instance.SetReady(Connection.ActiveChar, Ready);
    }
}
