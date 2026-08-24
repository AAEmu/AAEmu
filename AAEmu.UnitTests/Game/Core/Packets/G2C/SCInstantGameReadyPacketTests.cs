using System.Text;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.InstantGame;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SCInstantGameReadyPacketTests
{
    private static byte[] Body(params InstantGameRosterMember[] roster) =>
        new SCInstantGameReadyPacket(
                zoneInstanceId: new ZoneInstanceId(58, 0),
                type: InstantGameWireContract.NoBattleFieldType,
                now: 0x1122334455667788,
                roster: roster)
            .Write(new PacketStream())
            .GetBytes();

    [Test]
    public async Task Body_OrdersFieldsAsTheClientReadsThem()
    {
        var body = Body();

        await Assert.That(BitConverter.ToUInt32(body, 0)).IsEqualTo(58u);    // zi zone
        await Assert.That(BitConverter.ToUInt32(body, 4)).IsEqualTo(0u);     // zi instance
        await Assert.That(BitConverter.ToUInt32(body, 8)).IsEqualTo(0u);     // type
        await Assert.That(BitConverter.ToInt64(body, 12)).IsEqualTo(0x1122334455667788);
        await Assert.That(BitConverter.ToUInt16(body, 20)).IsEqualTo((ushort)0); // roster count
        await Assert.That(body.Length).IsEqualTo(22);
    }

    [Test]
    public async Task Body_CountsTheRosterAndWritesEachEntry()
    {
        var body = Body(new InstantGameRosterMember(2, 7, "Tester"));

        await Assert.That(BitConverter.ToUInt16(body, 20)).IsEqualTo((ushort)1);
        await Assert.That(body[22]).IsEqualTo((byte)2);                       // worldId
        await Assert.That(BitConverter.ToInt64(body, 23)).IsEqualTo(7L);      // side
        await Assert.That(BitConverter.ToUInt16(body, 31)).IsEqualTo((ushort)6); // name length
        await Assert.That(Encoding.UTF8.GetString(body, 33, 6)).IsEqualTo("Tester");
    }
}
