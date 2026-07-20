using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCharacterCreationFailedPacket(CharacterCreateError reason)
    : GamePacket(SCOffsets.SCCharacterCreationFailedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)reason);
        return stream;
    }
}
