using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Updates SquadBase isStarted / gameWorld. <c>gameStarted</c> writes isStarted; <c>destination</c>
/// writes gameWorld. Sent true on match enter and false when leaving the instance.
/// </summary>
public class SCSquadSetGameInfoPacket(sbyte destination, bool gameStarted) : GamePacket(SCOffsets.SCSquadSetGameInfoPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(destination);
        stream.Write(gameStarted);
        return stream;
    }
}
