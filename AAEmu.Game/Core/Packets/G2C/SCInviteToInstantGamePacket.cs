using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.InstantGame.Static;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCInviteToInstantGamePacket(
    ZoneInstanceId zoneInstanceId,
    uint rulesetId,
    InstantCorps corps,
    ulong qualifiedId)
    : GamePacket(SCOffsets.SCInviteToInstantGamePacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(zoneInstanceId);
        stream.Write(rulesetId);
        stream.Write((byte)corps);
        stream.Write(qualifiedId);
        return stream;
    }
}