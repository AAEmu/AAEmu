using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSNaviOpenPortalPacket() : GamePacket(CSOffsets.CSNaviOpenPortalPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var objId = stream.ReadBc();

        Logger.Warn("NaviOpenPortal, ObjId: {0}", objId);
    }
}
