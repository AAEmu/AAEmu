using AAEmu.Game.Models.Game.Units.Movements;

namespace AAEmu.UnitTests.Game.Models.Game.Units.Movements;

/// <summary>
/// Which movement records can be walked to their end. A batch is a run of records with no per-record
/// length, so one record whose tail is unreadable re-frames everything behind it.
/// </summary>
public class UnitMoveFramingRulesTests
{
    [Test]
    public async Task ThePushBlobFlag_MarksARecordAsUnframed()
    {
        await Assert.That(UnitMoveFramingRules.HasUnreadableTail(0x8000)).IsTrue();
    }

    [Test]
    public async Task TheOtherFlaggedTails_AreStillFramed()
    {
        // fall velocity, ground contact, gc id, climb data and the pushed-unit id all have a known
        // width, so those records parse to their end.
        await Assert.That(UnitMoveFramingRules.HasUnreadableTail(0x0000)).IsFalse();
        await Assert.That(UnitMoveFramingRules.HasUnreadableTail(0x0080)).IsFalse();
        await Assert.That(UnitMoveFramingRules.HasUnreadableTail(0x0020)).IsFalse();
        await Assert.That(UnitMoveFramingRules.HasUnreadableTail(0x0060)).IsFalse();
        await Assert.That(UnitMoveFramingRules.HasUnreadableTail(0x0040)).IsFalse();
        await Assert.That(UnitMoveFramingRules.HasUnreadableTail(0x0100)).IsFalse();
    }

    [Test]
    public async Task TheFlagIsReadAmongTheOthers()
    {
        // actor flags are a bitfield: 0x8000 alongside a walking flag is still unframed.
        await Assert.That(UnitMoveFramingRules.HasUnreadableTail(0x8005)).IsTrue();
    }
}
