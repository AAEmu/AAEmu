using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// The siege window asking for the teams fighting the siege it is showing. <see cref="Type"/> picks the view
/// it wants: every team at once (<see cref="AllTeamsMode"/>, answered with
/// <see cref="SCAllSiegeRaidTeamInfoPacket"/>), or a single faction's team with its members, which is a
/// different answer.
/// </summary>
public class CSAllSiegeRaidTeamInfoRequest() : GamePacket(CSOffsets.CSAllSiegeRaidTeamInfoRequest, 1)
{
    /// <summary>The view mode the window's refresh asks for: every team registered for the siege.</summary>
    public const short AllTeamsMode = 0;

    public short Type { get; private set; }

    public override void Read(PacketStream stream)
    {
        Type = stream.ReadInt16();

        var character = Connection.ActiveChar;
        if (character == null || Type != AllTeamsMode)
            return;

        // The teams are the ones fighting over the zone group the character is standing in, which is the id the
        // registration roster and the Dominion claim are both keyed by.
        var zone = ZoneManager.Instance.GetZoneByKey(character.Transform.ZoneId);
        if (zone == null)
            return;

        character.SendPacket(
            new SCAllSiegeRaidTeamInfoPacket(SiegeManager.Instance.GetRaidTeams((ushort)zone.GroupId)));
    }
}
