using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSRepairSlaveItemsPacket : GamePacket
{
    public CSRepairSlaveItemsPacket() : base(CSOffsets.CSRepairSlaveItemsPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var npcId = stream.ReadBc();

        Logger.Debug($"RepairSlaveItems, NpcId={npcId}");
    }
}
