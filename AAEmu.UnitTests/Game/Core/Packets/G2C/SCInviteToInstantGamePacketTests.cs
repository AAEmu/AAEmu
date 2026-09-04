using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.InstantGame;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SCInviteToInstantGamePacketTests
{
    private static byte[] Body(uint type = InstantGameWireContract.NoBattleFieldType,
        uint maxEntry = InstantGameWireContract.DungeonEnterDialogSelector) =>
        new SCInviteToInstantGamePacket(
                invitationTime: 60_000,
                zoneInstanceId: new ZoneInstanceId(58, 0),
                type: type,
                matchingKey: 0x1122334455667788,
                accept: 1,
                maxEntry: maxEntry)
            .Write(new PacketStream())
            .GetBytes();

    [Test]
    public async Task Body_OrdersFieldsAsTheClientReadsThem()
    {
        var body = Body();

        await Assert.That(BitConverter.ToUInt32(body, 0)).IsEqualTo(60_000u);   // invitationTime
        await Assert.That(BitConverter.ToUInt32(body, 4)).IsEqualTo(58u);       // zi zone
        await Assert.That(BitConverter.ToUInt32(body, 8)).IsEqualTo(0u);        // zi instance
        await Assert.That(BitConverter.ToUInt32(body, 12)).IsEqualTo(0u);       // type
        await Assert.That(BitConverter.ToUInt64(body, 16)).IsEqualTo(0x1122334455667788ul);
    }

    [Test]
    public async Task Body_PutsTheNestedBufferBeforeTheInvitationInfo()
    {
        // The client reads the nested buffer between the matching key and accept/maxEntry. Writing
        // the counts first makes it read the accept count as a buffer length and throw.
        var body = Body();
        const int blob = 24;

        await Assert.That(BitConverter.ToUInt16(body, blob)).IsEqualTo((ushort)4);
        await Assert.That(BitConverter.ToUInt16(body, blob + 2)).IsEqualTo((ushort)4);
        await Assert.That(BitConverter.ToUInt16(body, blob + 4)).IsEqualTo((ushort)2);
        await Assert.That(BitConverter.ToUInt16(body, blob + 6)).IsEqualTo((ushort)0);

        var info = blob + NestedBlobWire.HeaderSize;
        await Assert.That(BitConverter.ToUInt32(body, info)).IsEqualTo(1u);     // accept
        await Assert.That(BitConverter.ToUInt32(body, info + 4))
            .IsEqualTo(InstantGameWireContract.DungeonEnterDialogSelector);
        await Assert.That(body.Length).IsEqualTo(info + 8);
    }

    [Test]
    public async Task Body_CarriesTheNoBattleFieldTypeUnchanged()
    {
        // A dungeon invitation has no battle field. The client looks any other value up in its
        // battle field table and uses the result without checking it, so this exact value has to
        // survive to the wire.
        var body = Body(InstantGameWireContract.NoBattleFieldType);

        await Assert.That(BitConverter.ToUInt32(body, 12)).IsEqualTo(0u);
    }

    [Test]
    public async Task DungeonInvite_SelectsEnterInstanceDialogNotAllowTeamQueue()
    {
        // SetAskJoin opens DLG_TASK_JOIN_INSTANT_GAME ("Enter Instance") only when maxEntry == 1.
        // Any other value opens DLG_TASK_JOIN_INSTANT_GAME_INVITATION ("Allow Team Queue").
        var body = Body(maxEntry: InstantGameWireContract.DungeonEnterDialogSelector);
        var info = 24 + NestedBlobWire.HeaderSize;

        await Assert.That(BitConverter.ToUInt32(body, info + 4))
            .IsEqualTo(InstantGameWireContract.DungeonEnterDialogSelector);
    }
}
