using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Both id fields are 8 bytes on the wire; writing them as 4 bytes under-fills the body and
/// misaligns the trailing name string.
/// </summary>
public class SCExpeditionOwnerChangedPacket(uint id, uint id2, string charName)
    : GamePacket(SCOffsets.SCExpeditionOwnerChangedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((ulong)id);
        stream.Write((ulong)id2);
        stream.Write(charName);
        return stream;
    }
}
