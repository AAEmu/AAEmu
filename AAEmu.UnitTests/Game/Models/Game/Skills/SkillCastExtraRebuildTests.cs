using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// Remodel Confirm shares a start skill; the extra names the housing template. SkillStarted must
/// echo flag 0 — flag 7 is remodel on that packet and echoing it desyncs the stream.
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
            SupportItemId = 0x1122334455667788,
            AutoUseAaPoint = true
        });
        var body = stream.GetBytes();

        await Assert.That(body[0]).IsEqualTo((byte)6);
        await Assert.That(BitConverter.ToUInt64(body, 1)).IsEqualTo(0x1122334455667788ul);
        await Assert.That(body[9]).IsEqualTo((byte)1);
    }

    [Test]
    public async Task GradeEnchantSupport_Read_IsSupportItemIdThenAutoUse()
    {
        var stream = new PacketStream();
        stream.Write(0xA1B2C3D4E5F60708ul);
        stream.Write(true);

        var skillObject = SkillObject.GetByType(SkillObjectType.ItemGradeEnchantingSupport);
        skillObject.Read(new PacketStream(stream.GetBytes()));

        var support = skillObject as SkillObjectItemGradeEnchantingSupport;
        await Assert.That(support).IsNotNull();
        await Assert.That(support!.SupportItemId).IsEqualTo(0xA1B2C3D4E5F60708ul);
        await Assert.That(support.AutoUseAaPoint).IsTrue();
        await Assert.That(SkillObject.IsKnownType(6)).IsTrue();
        await Assert.That(SkillObject.IsKnownType(7)).IsFalse();
    }

    [Test]
    public async Task SocketExtract_Read_IsIndexThenIsAll()
    {
        var stream = new PacketStream();
        stream.Write(0);
        stream.Write(true);

        var skillObject = SkillObject.GetByType(SkillObjectType.ItemSocketExtract);
        skillObject.Read(new PacketStream(stream.GetBytes()));

        var extract = skillObject as SkillObjectItemSocketExtract;
        await Assert.That(extract).IsNotNull();
        await Assert.That(extract!.Index).IsEqualTo(0);
        await Assert.That(extract.IsAll).IsTrue();
        await Assert.That(SkillObject.IsKnownType(11)).IsTrue();
    }

    [Test]
    public async Task WriteSkillCastExtra_SocketExtract_EchoesFlagNone()
    {
        var stream = new PacketStream();
        stream.WriteSkillCastExtra(new SkillObjectItemSocketExtract
        {
            Flag = SkillObjectType.ItemSocketExtract,
            Index = 2,
            IsAll = true
        });
        var body = stream.GetBytes();

        await Assert.That(body[0]).IsEqualTo((byte)0);
    }
}
