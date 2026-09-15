using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSpecialtyRatioPacket() : GamePacket(CSOffsets.CSSpecialtyRatioPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var zoneGroupId = stream.ReadUInt16();
        var npcTemplateId = stream.ReadUInt32();

        Logger.Debug(
            "SpecialtyRatio, ZoneGroupId: {0}, NpcTemplateId: {1}",
            zoneGroupId,
            npcTemplateId);
        SpecialtyManager.Instance.SendRatioList(Connection.ActiveChar, zoneGroupId, npcTemplateId);
    }
}
