using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSWorldRaycastingPacket : GamePacket
{
    public CSWorldRaycastingPacket() : base(CSOffsets.CSWorldRaycastingPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        Logger.Debug("CSWorldRaycastingPacket");
    }
}
