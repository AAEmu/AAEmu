using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.InstantGame.Static;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCInstantGameAddPointPacket(
    ZoneInstanceId zoneInstanceId,
    InstantCorps corps,
    int points,
    int score1,
    int score2,
    string charName)
    : GamePacket(SCOffsets.SCInstantGameAddPointPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(zoneInstanceId);
        stream.Write((byte)corps);
        stream.Write(points);
        stream.Write(score1);
        stream.Write(score2);
        stream.Write(charName);

        return stream;
    }
}
