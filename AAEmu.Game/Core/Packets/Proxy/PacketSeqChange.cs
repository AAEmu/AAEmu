using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy;

public class PacketSeqChange() : GamePacket(PPOffsets.PacketSeqChange, 2)
{
    public override void Read(PacketStream stream)
    {
        var seq = stream.ReadByte();
    }
}
