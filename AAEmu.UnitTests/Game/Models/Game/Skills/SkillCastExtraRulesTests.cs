using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class SkillCastExtraRulesTests
{
    [Test]
    public async Task ReadsGradeEnchantSupport_OnlyWhenSkillCarriesGradeEnchant()
    {
        var regrade = new SkillTemplate
        {
            Effects =
            [
                new SkillEffect
                {
                    Template = new SpecialEffect { SpecialEffectTypeId = SpecialType.GradeEnchant }
                }
            ]
        };
        var temper = new SkillTemplate
        {
            Effects =
            [
                new SkillEffect
                {
                    Template = new SpecialEffect { SpecialEffectTypeId = SpecialType.ItemRefurbishment }
                }
            ]
        };

        await Assert.That(SkillCastExtraRules.ReadsGradeEnchantSupport(regrade)).IsTrue();
        await Assert.That(SkillCastExtraRules.ReadsGradeEnchantSupport(temper)).IsFalse();
        await Assert.That(SkillCastExtraRules.ReadsGradeEnchantSupport(null)).IsFalse();
        await Assert.That(SkillCastExtraRules.CarriesOptionalGradeEnchantSupport(temper)).IsTrue();
        await Assert.That(SkillCastExtraRules.CarriesOptionalGradeEnchantSupport(regrade)).IsFalse();

        // Regrade always reads the body; tempering only when u64+bool still sit on the stream.
        await Assert.That(SkillCastExtraRules.CanReadGradeEnchantSupportBody(regrade, 0)).IsTrue();
        await Assert.That(SkillCastExtraRules.CanReadGradeEnchantSupportBody(temper, 1)).IsFalse();
        await Assert.That(SkillCastExtraRules.CanReadGradeEnchantSupportBody(
            temper, SkillCastExtraRules.GradeEnchantSupportBodyBytes)).IsTrue();
    }

    [Test]
    public async Task SocketExtractHasIsAllByte_NeedsRoomForFlagAndInputDirection()
    {
        // Single-socket: index then only inputDirection → 1 byte left → no isAll on the wire.
        await Assert.That(SkillCastExtraRules.SocketExtractHasIsAllByte(1)).IsFalse();
        // Extract-all: index, isAll, inputDirection → 2+ bytes left.
        await Assert.That(SkillCastExtraRules.SocketExtractHasIsAllByte(2)).IsTrue();
    }

    [Test]
    public async Task SocketExtract_Read_SingleIndexDoesNotStealInputDirection()
    {
        var stream = new PacketStream();
        stream.Write(3); // socket index
        stream.Write((byte)7); // inputDirection — must stay unread by the skill object

        var skillObject = SkillObject.GetByType(SkillObjectType.ItemSocketExtract);
        skillObject.Read(new PacketStream(stream.GetBytes()));

        var extract = skillObject as SkillObjectItemSocketExtract;
        await Assert.That(extract).IsNotNull();
        await Assert.That(extract!.Index).IsEqualTo(3);
        await Assert.That(extract.IsAll).IsFalse();
    }

    [Test]
    public async Task SocketExtract_Read_IsAllTrueWhenBytePresent()
    {
        var stream = new PacketStream();
        stream.Write(0);
        stream.Write(true);
        stream.Write((byte)0); // inputDirection

        var skillObject = SkillObject.GetByType(SkillObjectType.ItemSocketExtract);
        skillObject.Read(new PacketStream(stream.GetBytes()));

        var extract = skillObject as SkillObjectItemSocketExtract;
        await Assert.That(extract).IsNotNull();
        await Assert.That(extract!.Index).IsEqualTo(0);
        await Assert.That(extract.IsAll).IsTrue();
    }
}
