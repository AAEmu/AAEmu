using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSRepairPetItemsPacket : GamePacket
{
    public CSRepairPetItemsPacket() : base(CSOffsets.CSRepairPetItemsPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var npcId = stream.ReadBc();

        Logger.Warn($"RepairPetItems, NpcId={npcId}");
    }
}
