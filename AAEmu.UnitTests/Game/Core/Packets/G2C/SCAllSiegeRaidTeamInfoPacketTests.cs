using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Sieges;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

/// <summary>
/// The siege window's team list. The head is the count the window sizes its list from, so a row we wrote past
/// it - or a count we wrote wrong - is what breaks the window, and both are pinned here.
/// </summary>
public class SCAllSiegeRaidTeamInfoPacketTests
{
    private static SiegeRaidTeam Team(uint factionId, uint team, ulong ownerId, string ownerName, bool defense,
        bool isWaitWar, int memberCount) =>
        new(factionId, team, ownerId, ownerName, defense, isWaitWar, memberCount);

    [Test]
    public async Task Write_LeadsWithTheRowCountThenTheTeamsInOrder()
    {
        SiegeRaidTeam[] teams =
        [
            Team(148, 1, 0, "", true, true, 7),
            Team(149, 2, 0, "", false, true, 3)
        ];

        var stream = new SCAllSiegeRaidTeamInfoPacket(teams).Write(new PacketStream());
        stream.Rollback();

        await Assert.That(stream.ReadInt32()).IsEqualTo(2);

        await Assert.That(stream.ReadUInt32()).IsEqualTo(148u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(1u);
        await Assert.That(stream.ReadUInt64()).IsEqualTo(0ul);
        await Assert.That(stream.ReadString()).IsEqualTo(string.Empty);
        await Assert.That(stream.ReadBoolean()).IsTrue();
        await Assert.That(stream.ReadBoolean()).IsTrue();
        await Assert.That(stream.ReadInt32()).IsEqualTo(7);

        await Assert.That(stream.ReadUInt32()).IsEqualTo(149u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(2u);
        await Assert.That(stream.ReadUInt64()).IsEqualTo(0ul);
        await Assert.That(stream.ReadString()).IsEqualTo(string.Empty);
        await Assert.That(stream.ReadBoolean()).IsFalse();
        await Assert.That(stream.ReadBoolean()).IsTrue();
        await Assert.That(stream.ReadInt32()).IsEqualTo(3);

        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task Write_CarriesTheTeamLeadersIdAndName()
    {
        // No commander election runs in this World yet, so this is always zero and empty today - pinned so the
        // row cannot quietly change shape when one lands.
        var stream = new SCAllSiegeRaidTeamInfoPacket([Team(148, 1, 4242, "Commander", true, false, 12)])
            .Write(new PacketStream());
        stream.Rollback();

        await Assert.That(stream.ReadInt32()).IsEqualTo(1);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(148u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(1u);
        await Assert.That(stream.ReadUInt64()).IsEqualTo(4242ul);
        await Assert.That(stream.ReadString()).IsEqualTo("Commander");
        await Assert.That(stream.ReadBoolean()).IsTrue();
        await Assert.That(stream.ReadBoolean()).IsFalse();
        await Assert.That(stream.ReadInt32()).IsEqualTo(12);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task Write_WithNoTeamsIsWhatNoRegistrationLooksLike()
    {
        var stream = new SCAllSiegeRaidTeamInfoPacket([]).Write(new PacketStream());
        stream.Rollback();

        await Assert.That(stream.ReadInt32()).IsEqualTo(0);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task Write_StopsAtTheNumberOfFramesTheWindowHas()
    {
        SiegeRaidTeam[] teams =
        [
            Team(148, 1, 0, "", true, false, 1),
            Team(149, 2, 0, "", false, false, 1),
            Team(114, 3, 0, "", false, false, 1),
            Team(161, 4, 0, "", false, false, 1)
        ];

        var stream = new SCAllSiegeRaidTeamInfoPacket(teams).Write(new PacketStream());
        stream.Rollback();

        await Assert.That(SCAllSiegeRaidTeamInfoPacket.MaxTeams).IsEqualTo(3);
        await Assert.That(stream.ReadInt32()).IsEqualTo(3);

        var seen = new List<uint>();
        for (var i = 0; i < SCAllSiegeRaidTeamInfoPacket.MaxTeams; i++)
        {
            seen.Add(stream.ReadUInt32());
            stream.ReadUInt32();
            stream.ReadUInt64();
            stream.ReadString();
            stream.ReadBoolean();
            stream.ReadBoolean();
            stream.ReadInt32();
        }

        await Assert.That(seen).IsEquivalentTo(new[] { 148u, 149u, 114u });
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task Write_OfANullListIsAnEmptyBoardRatherThanACrash()
    {
        var stream = new SCAllSiegeRaidTeamInfoPacket(null).Write(new PacketStream());
        stream.Rollback();

        await Assert.That(stream.ReadInt32()).IsEqualTo(0);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task ThePacketIsTheOpcodeTheWindowListensOn()
    {
        await Assert.That(new SCAllSiegeRaidTeamInfoPacket([]).TypeId).IsEqualTo((ushort)0x330);
    }
}
