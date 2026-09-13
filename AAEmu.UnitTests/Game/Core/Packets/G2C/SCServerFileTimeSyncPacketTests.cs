using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SCServerFileTimeSyncPacketTests
{
    private static readonly DateTime WinterUtc = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task Write_IsEightByteTimeFollowedBySignedFourByteBias()
    {
        const long worldFileTime = 0x1122334455667788;
        const int timeZoneBias = -330;

        var body = new SCServerFileTimeSyncPacket(worldFileTime, timeZoneBias)
            .Write(new PacketStream())
            .GetBytes();

        await Assert.That(body.Length).IsEqualTo(12);
        await Assert.That(BitConverter.ToInt64(body, 0)).IsEqualTo(worldFileTime);
        await Assert.That(BitConverter.ToInt32(body, 8)).IsEqualTo(timeZoneBias);
    }

    [Test]
    [Arguments(-480, 480)]
    [Arguments(330, -330)]
    [Arguments(0, 0)]
    public async Task ClientBias_IsMinutesWestOfUtc(int utcOffsetMinutes, int expectedBias)
    {
        var timeZone = TimeZoneInfo.CreateCustomTimeZone(
            $"fixed-{utcOffsetMinutes}",
            TimeSpan.FromMinutes(utcOffsetMinutes),
            "Test zone",
            "Test zone");

        var bias = SCServerFileTimeSyncPacket.GetClientTimeZoneBias(timeZone, WinterUtc);

        await Assert.That(bias).IsEqualTo(expectedBias);
    }

    [Test]
    public async Task ClientBias_UsesTheOffsetActiveAtTheSuppliedUtcTime()
    {
        var daylightStart = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0), 3, 2, DayOfWeek.Sunday);
        var daylightEnd = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0), 11, 1, DayOfWeek.Sunday);
        var adjustment = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2020, 1, 1),
            new DateTime(2030, 12, 31),
            TimeSpan.FromHours(1),
            daylightStart,
            daylightEnd);
        var timeZone = TimeZoneInfo.CreateCustomTimeZone(
            "western-dst",
            TimeSpan.FromHours(-5),
            "Western test zone",
            "Western standard time",
            "Western daylight time",
            [adjustment]);

        var winterBias = SCServerFileTimeSyncPacket.GetClientTimeZoneBias(timeZone, WinterUtc);
        var summerBias = SCServerFileTimeSyncPacket.GetClientTimeZoneBias(
            timeZone, new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc));

        await Assert.That(winterBias).IsEqualTo(300);
        await Assert.That(summerBias).IsEqualTo(240);
    }

    [Test]
    public async Task DefaultPacket_UsesCurrentUnixTimeAndLocalClientBias()
    {
        var beforeUtc = DateTime.UtcNow;
        var beforeUnixTime = Helpers.UnixTimeNow();
        var body = new SCServerFileTimeSyncPacket().Write(new PacketStream()).GetBytes();
        var afterUnixTime = Helpers.UnixTimeNow();
        var afterUtc = DateTime.UtcNow;

        var worldFileTime = BitConverter.ToInt64(body, 0);
        var timeZoneBias = BitConverter.ToInt32(body, 8);
        var expectedBefore = SCServerFileTimeSyncPacket.GetClientTimeZoneBias(TimeZoneInfo.Local, beforeUtc);
        var expectedAfter = SCServerFileTimeSyncPacket.GetClientTimeZoneBias(TimeZoneInfo.Local, afterUtc);

        await Assert.That(body.Length).IsEqualTo(12);
        await Assert.That(worldFileTime).IsGreaterThanOrEqualTo(beforeUnixTime);
        await Assert.That(worldFileTime).IsLessThanOrEqualTo(afterUnixTime);
        await Assert.That(timeZoneBias == expectedBefore || timeZoneBias == expectedAfter).IsTrue();
    }
}
