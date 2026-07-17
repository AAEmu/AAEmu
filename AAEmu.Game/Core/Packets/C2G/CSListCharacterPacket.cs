using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.Proxy;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSListCharacterPacket() : GamePacket(CSOffsets.CSListCharacterPacket, 5)
{
    public override void Read(PacketStream stream)
    {
        var size = stream.ReadInt32(); // TODO max size 4096
        var data = stream.ReadBytes(); // TODO or string?

        // Level 1 version - just send ChangeState for proxy
        Connection.SendPacket(new ChangeStatePacket(0));
    }
}
