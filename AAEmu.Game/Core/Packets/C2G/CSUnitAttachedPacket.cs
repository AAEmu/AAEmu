using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSUnitAttachedPacket : GamePacket
{
    public CSUnitAttachedPacket() : base(CSOffsets.CSUnitAttachedPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var objId = stream.ReadBc(); // character
        var objId2 = stream.ReadBc(); // doodad

        Logger.Warn("CSUnitAttachedPacket");
    }
}
