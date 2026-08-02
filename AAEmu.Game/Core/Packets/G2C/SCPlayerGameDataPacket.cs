using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// 10.0.2.13 body, named by its own serializer: u32 totalPlayTime, u64 createdTime. The old body sent a
/// client-data revision, a login timestamp and a zero, so the /played figure and the character's age were
/// both nonsense.
/// </remarks>
public class SCPlayerGameDataPacket(Character character) : GamePacket(SCOffsets.SCPlayerGameDataPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((uint)character.GetTotalPlayTimeSeconds());
        stream.Write(character.Created);

        return stream;
    }
}
