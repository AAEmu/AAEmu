using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// The siege raid-team window asking for the members of a team: its refresh and its member list both call it.
/// </summary>
/// <remarks>
/// The body is the pair the client has for the team it is showing — its own id for the team and the raid zone
/// it belongs to. This World keeps a roster per zone group rather than team entities, so the answer is resolved
/// from the character instead: the zone group they are standing in gives the raid zone, and their own alliance
/// gives the faction whose members are listed. The echoed pair is therefore not needed to find the rows.
/// </remarks>
public class CSSiegeRaidTeamInfoRequest() : GamePacket(CSOffsets.CSSiegeRaidTeamInfoRequest, 1)
{
    public ulong Type { get; private set; }
    public int Tid { get; private set; }

    public override void Read(PacketStream stream)
    {
        Type = stream.ReadUInt64();
        Tid = stream.ReadInt32();

        var character = Connection.ActiveChar;
        if (character == null)
            return;

        var zone = ZoneManager.Instance.GetZoneByKey(character.Transform.ZoneId);
        if (zone == null)
            return;

        // No raid-commander election runs in this World yet, so no member is the leader: the answer goes out
        // with a zero leader id, and the client leaves the commander's name empty because no row matches it.
        var schedule = SiegeGameData.Instance.GetSiegeZoneSchedule(zone.GroupId);
        character.SendPacket(new SCSiegeRaidTeamInfoPacket(
            0,
            (ushort)(schedule?.Id ?? 0),
            SiegeManager.Instance.GetRaidTeamMembers((ushort)zone.GroupId, SiegeManager.Instance.AllianceOfFaction(character))));
    }
}
