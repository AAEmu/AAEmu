using AAEmu.World.Core.Relay;

namespace AAEmu.UnitTests.World.Core.Relay;

/// <summary>
/// Reading a world's authored clock from its <c>time_of_day.xml</c>. The hour is the one the level
/// authored (instances are seeded with it instead of a value baked into the server); anything the level
/// does not author — a missing element, a missing or out-of-range hour — reads null so the caller reports
/// it rather than seeding a guess.
/// </summary>
public class ZoneTimeOfDayCatalogTests
{
    private const string ShippedShape =
        """<TimeOfDay Time="6.5772724" TimeStart="0" TimeEnd="24" TimeAnimSpeed="1.5" sRGB="1">""";

    [Test]
    public async Task ShippedShape_ReadsTheAuthoredHourAndSpeed()
    {
        var authored = ZoneTimeOfDayCatalog.Parse(ShippedShape);

        await Assert.That(authored).IsNotNull();
        await Assert.That(authored!.Value.StartHour).IsEqualTo(6.5772724f);
        await Assert.That(authored.Value.AnimSpeed).IsEqualTo(1.5f);
    }

    [Test]
    public async Task OtherAttributesBeforeTime_StillReadTheHour()
    {
        var authored = ZoneTimeOfDayCatalog.Parse(
            """<TimeOfDay sRGB="1" Time="18.25" TimeStart="0" TimeEnd="24">""");

        await Assert.That(authored).IsNotNull();
        await Assert.That(authored!.Value.StartHour).IsEqualTo(18.25f);
    }

    [Test]
    public async Task WithoutAnimationSpeed_SpeedReadsZero()
    {
        var authored = ZoneTimeOfDayCatalog.Parse("""<TimeOfDay Time="12" TimeStart="0" TimeEnd="24">""");

        await Assert.That(authored).IsNotNull();
        await Assert.That(authored!.Value.StartHour).IsEqualTo(12f);
        await Assert.That(authored.Value.AnimSpeed).IsEqualTo(0f);
    }

    [Test]
    public async Task StaticTime_ForcesClockSpeedZero()
    {
        // Static levels still author a non-zero TimeAnimSpeed; seeding that races the copy clock.
        var authored = ZoneTimeOfDayCatalog.Parse(
            """<TimeOfDay Time="22.5" TimeStart="0" TimeEnd="24" TimeAnimSpeed="1.5" UseStaticTime="1">""");

        await Assert.That(authored).IsNotNull();
        await Assert.That(authored!.Value.StartHour).IsEqualTo(22.5f);
        await Assert.That(authored.Value.AnimSpeed).IsEqualTo(0f);
    }

    [Test]
    public async Task MissingElement_ReadsNull()
    {
        await Assert.That(ZoneTimeOfDayCatalog.Parse("<Environment/>")).IsNull();
    }

    [Test]
    public async Task MissingHour_ReadsNull()
    {
        await Assert.That(ZoneTimeOfDayCatalog.Parse("""<TimeOfDay TimeStart="0" TimeEnd="24"/>""")).IsNull();
    }

    [Test]
    public async Task HourOutsideTheDay_ReadsNull()
    {
        // 24 and 25 are not hours in [0,24); wrapping them would light a different part of the day.
        await Assert.That(ZoneTimeOfDayCatalog.Parse("""<TimeOfDay Time="24"/>""")).IsNull();
        await Assert.That(ZoneTimeOfDayCatalog.Parse("""<TimeOfDay Time="25.5"/>""")).IsNull();
        await Assert.That(ZoneTimeOfDayCatalog.Parse("""<TimeOfDay Time="-1"/>""")).IsNull();
    }

    [Test]
    public async Task EmptyDocument_ReadsNull()
    {
        await Assert.That(ZoneTimeOfDayCatalog.Parse("")).IsNull();
        await Assert.That(ZoneTimeOfDayCatalog.Parse("   ")).IsNull();
    }
}
