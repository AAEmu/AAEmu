using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.InstantGame;
using AAEmu.Game.Models.Game.InstantGame.Static;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCInstantGameEndPacket(
    ZoneInstanceId zoneInstanceId,
    BattlefieldEndingReason battlefieldEndingReason,
    InstantGameTeamResult corps1Result,
    InstantGameTeamResult corps2Result)
    : GamePacket(SCOffsets.SCInstantGameEndPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(zoneInstanceId);
        stream.Write((byte)battlefieldEndingReason);
        stream.Write(corps1Result);
        stream.Write(corps2Result);

        return stream;
    }
}
