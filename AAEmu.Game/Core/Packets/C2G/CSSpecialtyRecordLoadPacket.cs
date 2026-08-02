using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSpecialtyRecordLoadPacket() : GamePacket(CSOffsets.CSSpecialtyRecordLoadPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var zoneGroupId = stream.ReadInt16();
        var itemId = stream.ReadUInt32();

        if (zoneGroupId > 0)
            SpecialtyManager.Instance.SendRecords(Connection.ActiveChar, (ushort)zoneGroupId, itemId);
    }
}
