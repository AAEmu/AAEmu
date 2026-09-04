using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>Queue ack for InstantGame / Indun matching. Wire: u32 type, u16 ErrorMessage.</summary>
public class SCAppliedToInstantGamePacket(uint type, ushort errorMessageId = 0)
    : GamePacket(SCOffsets.SCAppliedToInstantGamePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(type);
        stream.Write(errorMessageId);
        return stream;
    }
}
