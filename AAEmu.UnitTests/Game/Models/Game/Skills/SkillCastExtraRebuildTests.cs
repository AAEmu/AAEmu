using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// Remodel Confirm shares a start skill; the extra names the housing template. SkillStarted must
/// echo flag 0 — flag 7 is grade-enchant on that packet and a remodel extra there desyncs the stream.
/// </summary>
public class SkillCastExtraRebuildTests
{
    [Test]
    public async Task ForSkillStarted_KeepsTheHousingIdAndClearsTheFlag()
    {
        var extra = HousingRebuildSkillCast.ForSkillStarted(434);

        await Assert.That(HousingRebuildSkillCast.RequestedHousingId(extra)).IsEqualTo(434u);
        await Assert.That(extra.Flag).IsEqualTo(SkillObjectType.None);
    }

    [Test]
    public async Task ForSkillStarted_EmptyHousingId_IsABareSkillObject()
    {
        var extra = HousingRebuildSkillCast.ForSkillStarted(0);

        await Assert.That(extra).IsNotTypeOf<SkillObjectHousingRebuild>();
        await Assert.That(HousingRebuildSkillCast.RequestedHousingId(extra)).IsEqualTo(0u);
    }

    [Test]
    public async Task WriteSkillCastExtra_Rebuild_WritesFlagNone()
    {
        var stream = new PacketStream();
        stream.WriteSkillCastExtra(HousingRebuildSkillCast.ForSkillStarted(434));
        var body = stream.GetBytes();

        await Assert.That(body.Length).IsEqualTo(2);
        await Assert.That(body[0]).IsEqualTo((byte)0);
        await Assert.That(body[1]).IsEqualTo((byte)0);
    }

    [Test]
    public async Task WriteSkillCastExtra_GradeEnchant_StillWritesTheSupportExtra()
    {
        var stream = new PacketStream();
        stream.WriteSkillCastExtra(new SkillObjectItemGradeEnchantingSupport
        {
            Flag = SkillObjectType.ItemGradeEnchantingSupport,
            Id = 99,
            SupportItemId = 0x1122334455667788,
            AutoUseAaPoint = true
        });
        var body = stream.GetBytes();

        await Assert.That(body[0]).IsEqualTo((byte)SkillObjectType.ItemGradeEnchantingSupport);
        await Assert.That(BitConverter.ToUInt64(body, 1)).IsEqualTo(0x1122334455667788ul);
        await Assert.That(body[9]).IsEqualTo((byte)1);
    }
}
