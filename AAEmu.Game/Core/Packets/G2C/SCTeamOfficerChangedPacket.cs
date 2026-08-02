using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// </remarks>
public class SCTeamOfficerChangedPacket(int teamId, ulong memberId) : GamePacket(SCOffsets.SCTeamOfficerChangedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(teamId); // Native wire name: tid.
        stream.Write(memberId); // Native wire name: type.
        return stream;
    }
}
