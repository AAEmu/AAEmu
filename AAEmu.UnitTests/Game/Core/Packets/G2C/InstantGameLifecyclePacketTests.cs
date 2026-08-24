using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.InstantGame;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

/// <summary>
/// The packets that walk a joined match to "go". Their widths have to line up exactly, because the
/// client reads them off one shared cursor and a short field silently shifts every field after it.
/// </summary>
public class InstantGameLifecyclePacketTests
{
    private static ZoneInstanceId Zi => new(58, 3);

    [Test]
    public async Task CountDown_WritesZoneInstanceThenTimestamp()
    {
        var body = new SCInstantGameCountDownPacket(Zi, 0x1122334455667788)
            .Write(new PacketStream())
            .GetBytes();

        await Assert.That(BitConverter.ToUInt32(body, 0)).IsEqualTo(58u);
        await Assert.That(BitConverter.ToUInt32(body, 4)).IsEqualTo(3u);
        await Assert.That(BitConverter.ToInt64(body, 8)).IsEqualTo(0x1122334455667788);
        await Assert.That(body.Length).IsEqualTo(16);
    }

    [Test]
    public async Task Start_WritesTimestampAsSixtyFourBitsBeforeTheRound()
    {
        var body = new SCInstantGameStartPacket(Zi, 0x1122334455667788, InstantGameWireContract.FirstRound)
            .Write(new PacketStream())
            .GetBytes();

        await Assert.That(BitConverter.ToUInt32(body, 0)).IsEqualTo(58u);
        await Assert.That(BitConverter.ToUInt32(body, 4)).IsEqualTo(3u);
        await Assert.That(BitConverter.ToInt64(body, 8)).IsEqualTo(0x1122334455667788);
        await Assert.That(BitConverter.ToUInt32(body, 16)).IsEqualTo(InstantGameWireContract.FirstRound);
        await Assert.That(body.Length).IsEqualTo(20);
    }

    /// <summary>
    /// The instance id has to survive as its own field: it is what the client resolves against its
    /// catalogue to decide it is in a dungeon rather than a battle field.
    /// </summary>
    [Test]
    public async Task Reentry_CarriesTheInstanceIdAheadOfTheMatchType()
    {
        var body = new SCInstantGameReentryPacket(Zi, instanceId: 23,
                type: InstantGameWireContract.NoBattleFieldType, serverStart: 0x1122334455667788)
            .Write(new PacketStream())
            .GetBytes();

        await Assert.That(BitConverter.ToUInt32(body, 0)).IsEqualTo(58u);
        await Assert.That(BitConverter.ToUInt32(body, 4)).IsEqualTo(3u);
        await Assert.That(BitConverter.ToUInt32(body, 8)).IsEqualTo(23u);
        await Assert.That(BitConverter.ToUInt32(body, 12)).IsEqualTo(0u);
        await Assert.That(BitConverter.ToInt64(body, 16)).IsEqualTo(0x1122334455667788);
        await Assert.That(BitConverter.ToUInt16(body, 24)).IsEqualTo((ushort)0);
        await Assert.That(body.Length).IsEqualTo(26);
    }

    /// <summary>
    /// These four sit next to each other in the opcode space and carry different bodies, so a pair
    /// swapped between them still frames and still parses, and only shows up as a match that hangs on
    /// the wrong screen. Ready and CountDown were swapped once already.
    /// </summary>
    [Test]
    public async Task LifecycleOpcodesAreDistinctAndInTheOrderTheClientRegistersThem()
    {
        await Assert.That(SCOffsets.SCInstantGameJoinedPacket).IsEqualTo((ushort)0x1D7);
        await Assert.That(SCOffsets.SCInstantGameCountDownPacket).IsEqualTo((ushort)0x1D8);
        await Assert.That(SCOffsets.SCInstantGameReadyPacket).IsEqualTo((ushort)0x1D9);
        await Assert.That(SCOffsets.SCInstantGameStartPacket).IsEqualTo((ushort)0x1DA);
        await Assert.That(SCOffsets.SCInstantGameReentryPacket).IsEqualTo((ushort)0x1E7);
    }

    /// <summary>
    /// A dungeon has no battle field behind it, and the client looks the value up in a table that
    /// starts at one, so anything other than the sentinel dereferences a miss.
    /// </summary>
    [Test]
    public async Task Joined_SendsTheNoBattleFieldSentinelForADungeon()
    {
        var body = new SCInstantGameJoinedPacket(Zi, InstantGameWireContract.NoBattleFieldType)
            .Write(new PacketStream())
            .GetBytes();

        await Assert.That(BitConverter.ToUInt32(body, 8)).IsEqualTo(0u);
        await Assert.That(body.Length).IsEqualTo(12);
    }
}
