using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.InstantGame;

/// <summary>
/// A participant on the roster the client keeps for a match's scoreboard. A dungeon has no
/// scoreboard and sends an empty roster.
/// </summary>
/// <param name="WorldId">World the participant plays on.</param>
/// <param name="Type">Side the participant belongs to, in the match's own id space.</param>
/// <param name="Name">Display name shown on the scoreboard.</param>
public readonly record struct InstantGameRosterMember(byte WorldId, long Type, string Name);

/// <summary>
/// The roster tail shared by the packets that hand a client its match, written the way the client
/// reads it: a count, then that many entries.
/// </summary>
public static class InstantGameRosterWire
{
    public static PacketStream Write(PacketStream stream, IReadOnlyList<InstantGameRosterMember> roster)
    {
        var members = roster ?? [];
        stream.Write((ushort)members.Count);
        foreach (var member in members)
        {
            stream.Write(member.WorldId);
            stream.Write(member.Type);
            stream.Write(member.Name);
        }

        return stream;
    }
}
