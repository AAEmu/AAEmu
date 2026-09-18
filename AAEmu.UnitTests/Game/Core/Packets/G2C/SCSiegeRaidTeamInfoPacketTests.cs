using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

/// <summary>
/// One faction's raid team for a siege. The head carries the leader's id (the client names the row that
/// matches it), the raid zone it fights in and the row count the client loops for, so both the count and every
/// row's widths are what break the member list if they move.
/// </summary>
public class SCSiegeRaidTeamInfoPacketTests
{
    private static SiegeRaidTeamMemberInfo Member(ulong id, string name, byte level, byte heirLevel,
        byte ability1, byte ability2, byte ability3, uint gearScore) =>
        new(id, name, level, heirLevel, ability1, ability2, ability3, gearScore);

    [Test]
    public async Task Write_LeadsWithTheLeaderTheRaidZoneAndTheRowCount()
    {
        var stream = new SCSiegeRaidTeamInfoPacket(4242, 2, [Member(8, "Tester", 55, 3, 1, 4, 8, 9412)])
            .Write(new PacketStream());
        stream.Rollback();

        await Assert.That(stream.ReadUInt64()).IsEqualTo(4242ul);
        await Assert.That(stream.ReadUInt16()).IsEqualTo((ushort)2);
        await Assert.That(stream.ReadInt32()).IsEqualTo(1);

        await Assert.That(stream.ReadUInt64()).IsEqualTo(8ul);
        await Assert.That(stream.ReadString()).IsEqualTo("Tester");
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)55);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)3);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)1);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)4);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)8);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(9412u);

        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task Write_CarriesOneRowPerMemberInOrder()
    {
        SiegeRaidTeamMemberInfo[] members =
        [
            Member(8, "Tester", 55, 3, 1, 4, 8, 9412),
            Member(39, "Temu", 54, 0, 7, 30, 30, 12000)
        ];

        var stream = new SCSiegeRaidTeamInfoPacket(0, 2, members).Write(new PacketStream());
        stream.Rollback();

        await Assert.That(stream.ReadUInt64()).IsEqualTo(0ul);
        await Assert.That(stream.ReadUInt16()).IsEqualTo((ushort)2);
        await Assert.That(stream.ReadInt32()).IsEqualTo(2);

        for (var i = 0; i < members.Length; i++)
        {
            await Assert.That(stream.ReadUInt64()).IsEqualTo(members[i].CharacterId);
            await Assert.That(stream.ReadString()).IsEqualTo(members[i].Name);
            await Assert.That(stream.ReadByte()).IsEqualTo(members[i].Level);
            await Assert.That(stream.ReadByte()).IsEqualTo(members[i].HeirLevel);
            await Assert.That(stream.ReadByte()).IsEqualTo(members[i].Ability1);
            await Assert.That(stream.ReadByte()).IsEqualTo(members[i].Ability2);
            await Assert.That(stream.ReadByte()).IsEqualTo(members[i].Ability3);
            await Assert.That(stream.ReadUInt32()).IsEqualTo(members[i].GearScore);
        }

        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task Write_WithNoMembersIsWhatANobodyRegisteredTeamLooksLike()
    {
        var stream = new SCSiegeRaidTeamInfoPacket(0, 1, []).Write(new PacketStream());
        stream.Rollback();

        await Assert.That(stream.ReadUInt64()).IsEqualTo(0ul);
        await Assert.That(stream.ReadUInt16()).IsEqualTo((ushort)1);
        await Assert.That(stream.ReadInt32()).IsEqualTo(0);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task Write_OfANullListWritesTheHeadOnly()
    {
        var stream = new SCSiegeRaidTeamInfoPacket(7, 3, null).Write(new PacketStream());
        stream.Rollback();

        await Assert.That(stream.ReadUInt64()).IsEqualTo(7ul);
        await Assert.That(stream.ReadUInt16()).IsEqualTo((ushort)3);
        await Assert.That(stream.ReadInt32()).IsEqualTo(0);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task ThePacketIsTheOpcodeTheMemberListListensOn()
    {
        await Assert.That(new SCSiegeRaidTeamInfoPacket(0, 0, []).TypeId).IsEqualTo((ushort)0x32E);
    }
}
