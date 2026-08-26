using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Squad;

using SquadModel = AAEmu.Game.Models.Game.Squad.Squad;

namespace AAEmu.UnitTests.Game.Models.Game.Squads;

public class SquadRulesTests
{
    private static SquadModel MakeSquad(uint id, uint catalogId, SquadOpenType openType, uint max = 5)
    {
        var squad = new SquadModel
        {
            Id = id,
            CatalogId = catalogId,
            OpenType = openType,
            MaxMembers = max,
            LeaderCharacterId = 1
        };
        squad.Members.Add(new SquadMember { CharacterId = 1, Name = "Leader", Level = 55, IsLeader = true });
        return squad;
    }

    [Test]
    public async Task CanCreate_RejectsWhenAlreadyInSquad()
    {
        await Assert.That(SquadRules.CanCreate(alreadyInSquad: true)).IsFalse();
        await Assert.That(SquadRules.CanCreate(alreadyInSquad: false)).IsTrue();
    }

    [Test]
    public async Task CallerOwnsInvite_RejectsForeignRefuse()
    {
        await Assert.That(SquadRules.CallerOwnsInvite(inviteTargetId: 42, callerCharacterId: 42)).IsTrue();
        await Assert.That(SquadRules.CallerOwnsInvite(inviteTargetId: 42, callerCharacterId: 7)).IsFalse();
        await Assert.That(SquadRules.CallerOwnsInvite(inviteTargetId: 0, callerCharacterId: 0)).IsFalse();
    }

    [Test]
    public async Task FilterListed_OmitsQuickEnterAndWrongCatalog()
    {
        // Private Recruit teams are on the board too — the recruit method only decides whether a
        // browsing player may apply. Quick Enter is the one method that never shows up.
        var all = new List<SquadModel>
        {
            MakeSquad(1, 23, SquadOpenType.Public),
            MakeSquad(2, 23, SquadOpenType.Private),
            MakeSquad(3, 23, SquadOpenType.MustPublic),
            MakeSquad(4, 23, SquadOpenType.DirectMatching),
            MakeSquad(5, 99, SquadOpenType.Public)
        };
        var listed = SquadRules.FilterListed(all, 23);
        await Assert.That(listed.Count).IsEqualTo(3);
        await Assert.That(listed.Select(s => s.Id)).IsEquivalentTo([1u, 2u, 3u]);
    }

    [Test]
    public async Task PrivateRecruit_IsListedButRefusesApplications()
    {
        var squad = MakeSquad(1, 23, SquadOpenType.Private);
        await Assert.That(SquadRules.IsListedOpenType(squad.OpenType)).IsTrue();
        await Assert.That(SquadRules.CanJoinPublic(squad, 2, level: 55, 0)).IsFalse();
    }

    [Test]
    public async Task CanJoinPublic_EnforcesLevelAndCapacity()
    {
        var squad = MakeSquad(1, 23, SquadOpenType.Public, max: 2);
        squad.LimitLevel = 50;
        await Assert.That(SquadRules.CanJoinPublic(squad, 2, level: 49, 0)).IsFalse();
        await Assert.That(SquadRules.CanJoinPublic(squad, 2, level: 50, 0)).IsTrue();
        squad.Members.Add(new SquadMember { CharacterId = 2, Name = "B", Level = 55 });
        await Assert.That(SquadRules.CanJoinPublic(squad, 3, level: 55, 0)).IsFalse();
    }

    [Test]
    public async Task OneCharacter_CannotJoinTwice()
    {
        var squad = MakeSquad(1, 23, SquadOpenType.Public);
        await Assert.That(SquadRules.CanJoinPublic(squad, 1, level: 55, 0)).IsFalse();
    }

    [Test]
    public async Task GearScoreGate_BlocksApplicantsBelowLimit()
    {
        var squad = MakeSquad(1, 23, SquadOpenType.Public);
        squad.LimitGearScore = 5000;

        await Assert.That(SquadRules.CanJoinPublic(squad, 2, level: 55, characterGearScore: 4999)).IsFalse();
        await Assert.That(SquadRules.CanJoinPublic(squad, 2, level: 55, characterGearScore: 5000)).IsTrue();
        // No limit set: any score passes.
        squad.LimitGearScore = 0;
        await Assert.That(SquadRules.CanJoinPublic(squad, 2, level: 55, characterGearScore: 0)).IsTrue();
    }

    [Test]
    public async Task EveryRecruitMethod_QueuesMatchingAndNeverEntersOnRegister()
    {
        // Registering only queues — including Quick Enter, which skips waiting for other players
        // but still waits for the instance. Entering here created a dungeon off the Register
        // button and dropped the player straight into it.
        foreach (var openType in Enum.GetValues<SquadOpenType>())
        {
            var squad = MakeSquad(1, 23, openType);
            squad.Members[0].Ready = true;
            await Assert.That(SquadRules.ShouldQueueMatchingOnApply(squad)).IsTrue();
        }
    }

    [Test]
    public async Task AlreadyEntered_DoesNotQueueAgain()
    {
        var squad = MakeSquad(1, 23, SquadOpenType.DirectMatching);
        squad.Members[0].Ready = true;
        squad.EnterCommitted = true;
        await Assert.That(SquadRules.ShouldQueueMatchingOnApply(squad)).IsFalse();
    }

    [Test]
    public async Task ResetAfterInstanceLeave_AllowsRegisterAgain()
    {
        var squad = MakeSquad(1, 23, SquadOpenType.DirectMatching);
        squad.Members[0].Ready = true;
        squad.EnterCommitted = true;
        squad.MatchingApplied = true;
        squad.Joining = true;

        SquadRules.ResetAfterInstanceLeave(squad);

        await Assert.That(squad.EnterCommitted).IsFalse();
        await Assert.That(squad.MatchingApplied).IsFalse();
        await Assert.That(squad.Joining).IsFalse();
        await Assert.That(squad.Members[0].Ready).IsFalse();
        squad.Members[0].Ready = true;
        await Assert.That(SquadRules.ShouldQueueMatchingOnApply(squad)).IsTrue();
    }

    [Test]
    public async Task QuickEnter_DisbandsAfterInstanceLeave_ListedKeepsTeam()
    {
        await Assert.That(SquadRules.ShouldDisbandAfterInstanceLeave(SquadOpenType.DirectMatching))
            .IsTrue();
        await Assert.That(SquadRules.ShouldDisbandAfterInstanceLeave(SquadOpenType.Public))
            .IsFalse();
        await Assert.That(SquadRules.ShouldDisbandAfterInstanceLeave(SquadOpenType.Private))
            .IsFalse();
    }

    [Test]
    public async Task OnlyRecruitMethods_WaitForOtherPlayers()
    {
        await Assert.That(SquadRules.WaitsForOtherPlayers(SquadOpenType.DirectMatching)).IsFalse();
        await Assert.That(SquadRules.WaitsForOtherPlayers(SquadOpenType.Public)).IsTrue();
        await Assert.That(SquadRules.WaitsForOtherPlayers(SquadOpenType.Private)).IsTrue();
        await Assert.That(SquadRules.WaitsForOtherPlayers(SquadOpenType.MustPublic)).IsTrue();
    }

    [Test]
    public async Task UnreadyMember_HoldsTheTeamOutOfTheQueue()
    {
        var squad = MakeSquad(1, 23, SquadOpenType.Public);
        await Assert.That(SquadRules.ShouldQueueMatchingOnApply(squad)).IsFalse();
    }

    [Test]
    public async Task Page_RespectsPageSize()
    {
        var listed = Enumerable.Range(1, 7)
            .Select(i => MakeSquad((uint)i, 23, SquadOpenType.Public))
            .ToList();
        var (page0, total) = SquadRules.Page(listed, 0);
        await Assert.That(total).IsEqualTo(7);
        await Assert.That(page0.Count).IsEqualTo(3);
        var (page2, _) = SquadRules.Page(listed, 2);
        await Assert.That(page2.Count).IsEqualTo(1);
    }

    [Test]
    public async Task DisbandClearsMembership_ViaRulesFilter()
    {
        var all = new List<SquadModel> { MakeSquad(1, 23, SquadOpenType.Public) };
        all.Clear();
        await Assert.That(SquadRules.FilterListed(all, 23).Count).IsEqualTo(0);
    }

    [Test]
    public async Task CreateListPacket_WriteSmoke()
    {
        var entry = new SquadListEntry
        {
            SquadId = 7,
            OpenType = SquadOpenType.Public,
            OwnerName = "Tester",
            OwnerLevel = 55,
            WorldName = "1",
            ExplanationText = "",
            LimitLevel = 50,
            LimitGearScore = 0,
            Field = new SquadFieldType(SquadFieldType.ZoneGroupKind, InstanceId: 23, Value: 58),
            CatalogWireId = 23,
            LeaderWorldCharKey = 1001,
            PublicKey = 7,
            Members = [],
            IsMySquad = true,
            ButtonEnable = false,
            ButtonType = 2
        };
        var create = new SCCreateSquadPacket(false, entry);
        var list = new SCSelectSquadListPacket(1, 0, [entry]);
        var emptyList = new SCSelectSquadListPacket(0, 0, []);
        var createBytes = create.Write(new PacketStream());
        var listBytes = list.Write(new PacketStream());
        var emptyBytes = emptyList.Write(new PacketStream());
        const int rowSize = 85; // SquadBase mask 0x0F, empty explanation, mask-8 count 0
        const int header = NestedBlobWire.HeaderSize;

        // ignoreMinGameSize(1) + nested header + one row
        await Assert.That(createBytes.Count).IsEqualTo(1 + header + rowSize);
        // available(4) + curPage(4) + nested header + u32 count + one row
        await Assert.That(listBytes.Count).IsEqualTo(8 + header + 4 + rowSize);
        // Same, with a count of zero and no rows.
        await Assert.That(emptyBytes.Count).IsEqualTo(8 + header + 4);
    }

    [Test]
    public async Task SquadBaseRow_PlacesFieldKindInstanceAndValueWhereClientReadsThem()
    {
        // The field-type blob is u32 kind, u32 instanceId, u64 value. The kind drives the
        // title and member-cap lookups; the instance id sits between them and is what the
        // client's matchmaking check resolves before it will register a squad.
        var entry = new SquadListEntry
        {
            SquadId = 1,
            Field = new SquadFieldType(SquadFieldType.ZoneGroupKind, InstanceId: 23, Value: 58),
            CatalogWireId = 23
        };
        var row = entry.Write(new PacketStream()).GetBytes();

        await Assert.That(BitConverter.ToUInt32(row, 0)).IsEqualTo(1u);    // squadId
        await Assert.That(BitConverter.ToUInt32(row, 12)).IsEqualTo(2u);   // kind
        await Assert.That(BitConverter.ToUInt32(row, 16)).IsEqualTo(23u);  // instance id
        await Assert.That(BitConverter.ToUInt64(row, 20)).IsEqualTo(58ul); // zone group id
        await Assert.That(BitConverter.ToUInt32(row, 28)).IsEqualTo(23u);  // catalog id
    }

    [Test]
    public async Task SquadBaseRow_KeepsMatchingAndStartedFlagsDistinct()
    {
        // The two bytes after the matching block are isStarted and gameWorld, not a packed
        // member count. Writing a count there tells the client the game already began, and it
        // then refuses to enter the instance.
        var entry = new SquadListEntry
        {
            SquadId = 1,
            Field = new SquadFieldType(SquadFieldType.ZoneGroupKind, InstanceId: 23, Value: 58),
            MatchingKey = 1,
            IsJoining = true,
            GameWorldId = 3,
            PublicKey = 9
        };
        var row = entry.Write(new PacketStream()).GetBytes();

        // squadId(4) header(8) fieldType(16) catalog(4) leader(8) empty explanation(2)
        const int matchingBlock = 42;
        await Assert.That(BitConverter.ToUInt64(row, matchingBlock)).IsEqualTo(1ul); // matching key
        await Assert.That(row[matchingBlock + 8]).IsEqualTo((byte)1);                // isJoining
        await Assert.That(row[matchingBlock + 16]).IsEqualTo((byte)0);               // isStarted
        await Assert.That(row[matchingBlock + 17]).IsEqualTo((byte)3);               // gameWorld
        await Assert.That(BitConverter.ToUInt32(row, matchingBlock + 18)).IsEqualTo(9u);
    }

    [Test]
    public async Task FieldTypeWire_RoundTripsAllThreeFields()
    {
        // A round trip proves we keep the value the client sent instead of dropping it, which
        // is what previously left the instance id at zero on the way back out.
        var written = new PacketStream();
        written.Write((byte)SquadFieldType.ZoneGroupKind);
        written.Write(23u);
        written.Write(58ul);

        var field = SquadFieldTypeWire.Read(new PacketStream(written.GetBytes()));

        await Assert.That(field.Kind).IsEqualTo((byte)SquadFieldType.ZoneGroupKind);
        await Assert.That(field.InstanceId).IsEqualTo(23u);
        await Assert.That(field.Value).IsEqualTo(58ul);
    }

    [Test]
    public async Task WorldCharKey_CarriesWorldIdInByteFour()
    {
        // The client reads byte 4 of the key back out as the world id and names the member's
        // server from it, so a bare character id leaves that column blank.
        var key = SquadWorldCharKey.Make(1001, 3);

        await Assert.That(SquadWorldCharKey.GetCharacterId(key)).IsEqualTo(1001u);
        await Assert.That(SquadWorldCharKey.GetWorldId(key)).IsEqualTo((byte)3);
        await Assert.That(BitConverter.GetBytes(key)[4]).IsEqualTo((byte)3);
    }

    [Test]
    public async Task EmbeddedMask8_LeaderKeyFollowsMembersAtExactOffset()
    {
        // Members are fixed width with no padding, and the leader key sits immediately after
        // them. One stray byte per member would shift that key past the blob and throw.
        var entry = new SquadListEntry
        {
            SquadId = 1,
            WorldId = 3,
            LeaderWorldCharKey = SquadWorldCharKey.Make(1001, 3),
            Members =
            [
                new SquadMember { CharacterId = 1001, Level = 55, IsLeader = true },
                new SquadMember { CharacterId = 1002, Level = 50 }
            ]
        };
        var row = entry.Write(new PacketStream()).GetBytes();

        // An empty row is 85 bytes: mask 1, open type, a zero member count and the leader key.
        const int leaderKeySize = 8;
        const int firstMember = 85 - leaderKeySize;
        var afterMembers = firstMember + (2 * SquadMemberWire.EmbeddedMask8PayloadSize);

        await Assert.That(row.Length).IsEqualTo(afterMembers + leaderKeySize);
        // Leader key must match the first member's key so the client can resolve the leader.
        await Assert.That(BitConverter.ToUInt64(row, afterMembers))
            .IsEqualTo(BitConverter.ToUInt64(row, firstMember));
    }

    [Test]
    public async Task NestedBlobHeader_CountsTagAndPayload()
    {
        var payload = new PacketStream();
        payload.Write((uint)0); // 4-byte payload

        var stream = new PacketStream();
        NestedBlobWire.Write(stream, payload);
        var bytes = stream.GetBytes();

        await Assert.That(bytes.Length).IsEqualTo(NestedBlobWire.HeaderSize + 4);
        // Transport size covers the 4-byte prologue the client's buffer owns.
        await Assert.That(BitConverter.ToUInt16(bytes, 0)).IsEqualTo((ushort)8);
        await Assert.That(BitConverter.ToUInt16(bytes, 2)).IsEqualTo((ushort)8);
        // Inner size covers tag + payload, so the reader's limit lands past its start cursor.
        await Assert.That(BitConverter.ToUInt16(bytes, 4)).IsEqualTo((ushort)6);
        await Assert.That(BitConverter.ToUInt16(bytes, 6)).IsEqualTo((ushort)0);
    }

    [Test]
    public async Task NestedBlobHeader_EmptyPayloadMatchesDefaultBuffer()
    {
        var stream = new PacketStream();
        NestedBlobWire.WriteEmpty(stream);
        var bytes = stream.GetBytes();

        await Assert.That(bytes.Length).IsEqualTo(NestedBlobWire.HeaderSize);
        await Assert.That(BitConverter.ToUInt16(bytes, 0)).IsEqualTo((ushort)4);
        await Assert.That(BitConverter.ToUInt16(bytes, 4)).IsEqualTo((ushort)2);
    }

    [Test]
    public async Task EmbeddedMask8_WritesWorldCharKeyAsU64()
    {
        var stream = new PacketStream();
        SquadMemberWire.WriteEmbeddedMask8(stream, new SquadMember
        {
            CharacterId = 1001,
            Level = 55,
            Ability1 = 1,
            Ability2 = 2,
            Ability3 = 3
        }, worldId: 3);
        var bytes = stream.GetBytes();

        await Assert.That(bytes.Length).IsEqualTo(SquadMemberWire.EmbeddedMask8PayloadSize);
        await Assert.That(BitConverter.ToUInt64(bytes, 0))
            .IsEqualTo(SquadWorldCharKey.Make(1001, 3));
        await Assert.That(bytes[8]).IsEqualTo((byte)55);
    }
}
