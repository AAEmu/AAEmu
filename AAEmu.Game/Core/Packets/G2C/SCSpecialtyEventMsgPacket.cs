using System.Text;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public sealed class SCSpecialtyEventMsgPacket(string message)
    : GamePacket(SCOffsets.SCSpecialtyEventMsgPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        if (Encoding.UTF8.GetByteCount(message) > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(message), "A specialty event message can contain at most 255 UTF-8 bytes.");
        stream.Write(message);
        return stream;
    }
}
