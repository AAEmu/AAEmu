using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCharacterCreationFailedPacket : GamePacket
{
    private readonly CharacterCreateError _reason;

    public SCCharacterCreationFailedPacket(CharacterCreateError reason) : base(SCOffsets.SCCharacterCreationFailedPacket, 5)
    {
        _reason = reason;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)_reason);
        return stream;
    }
}
