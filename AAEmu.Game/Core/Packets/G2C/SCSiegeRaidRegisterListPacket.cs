using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// The characters registered to fight a siege on one side, as the registration popup lists them.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value: a registration flag, a list-updated flag, the zone group type
/// the client asked about, then one block per zone group holding its rows. A row's ranking is its
/// place in the list; the two trailing values have no name the client reveals, so they go out as
/// zero rather than as a guess.
/// </remarks>
public class SCSiegeRaidRegisterListPacket(
    bool registState,
    bool registInfoList,
    ushort type,
    IReadOnlyList<SiegeRaidRegisterZone> zones)
    : GamePacket(SCOffsets.SCSiegeRaidRegisterListPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(registState);
        stream.Write(registInfoList);
        stream.Write(type);

        var zoneRows = zones ?? [];
        stream.Write((int)zoneRows.Count);
        foreach (var zone in zoneRows)
        {
            stream.Write(zone.ZoneGroupType);
            var rows = zone.Rows ?? [];
            stream.Write((int)rows.Count);
            foreach (var row in rows)
            {
                stream.Write(row.Ranking);
                stream.Write(row.CharName ?? string.Empty);
                stream.Write(row.CharId);
                stream.Write(row.Unnamed1);
                stream.Write(row.Unnamed2);
            }
        }

        return stream;
    }
}

/// <summary>One zone group's block of <see cref="SCSiegeRaidRegisterListPacket"/>.</summary>
public readonly record struct SiegeRaidRegisterZone(int ZoneGroupType, IReadOnlyList<SiegeRaidRegisterRow> Rows);

/// <summary>One registered character of a siege raid team.</summary>
public readonly record struct SiegeRaidRegisterRow(
    uint Ranking,
    string CharName,
    long CharId,
    uint Unnamed1 = 0,
    byte Unnamed2 = 0);
