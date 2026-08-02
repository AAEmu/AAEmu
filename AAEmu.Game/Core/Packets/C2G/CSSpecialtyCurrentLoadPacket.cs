using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSpecialtyCurrentLoadPacket() : GamePacket(CSOffsets.CSSpecialtyCurrentLoadPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var fromZoneGroupId = stream.ReadInt16();
        var toZoneGroupId = stream.ReadInt16();
        if (fromZoneGroupId <= 0 || toZoneGroupId <= 0)
            return;

        SpecialtyManager.Instance.SendCurrentRatios(
            Connection.ActiveChar,
            (ushort)fromZoneGroupId,
            (ushort)toZoneGroupId);
    }
}
