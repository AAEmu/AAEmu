using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

/// <summary>
/// Pins the body reads: type i32, bc, then the flagged tail of.
/// </summary>
public class SCQuestUnitReqFailedPacketTests
{
    private const uint QuestId = 9432;
    private const uint ObjId = 0x1A2B3C;

    private static byte[] Bytes(UnitReqsValidationResult result) =>
        new SCQuestUnitReqFailedPacket(QuestId, ObjId, result).Write(new PacketStream()).GetBytes();

    [Test]
    public async Task Opcode_IsTheQuestUnitReqFailedSlot()
    {
        var packet = new SCQuestUnitReqFailedPacket(QuestId, ObjId, new UnitReqsValidationResult(SkillResultKeys.ok, 0, 0));

        await Assert.That(packet.TypeId).IsEqualTo((ushort)0x18D);
        await Assert.That(packet.TypeId).IsEqualTo(SCOffsets.SCQuestUnitReqFailedPacket);
    }

    [Test]
    public async Task Body_IsQuestIdThenObjIdThenTheFlaggedTail()
    {
        var body = Bytes(new UnitReqsValidationResult(SkillResultKeys.skill_urk_level, 0, 0));

        // 4 (type) + 3 (bc) + 1 (flags) + 1 (result byte): the u16, u32 and gate are at their defaults.
        await Assert.That(body.Length).IsEqualTo(9);
        await Assert.That(BitConverter.ToUInt32(body, 0)).IsEqualTo(QuestId);
        await Assert.That(body[4]).IsEqualTo((byte)0x3C);
        await Assert.That(body[5]).IsEqualTo((byte)0x2B);
        await Assert.That(body[6]).IsEqualTo((byte)0x1A);
        await Assert.That(body[7]).IsEqualTo((byte)0x01);
        await Assert.That(body[8]).IsEqualTo((byte)SkillResult.UrkLevel);
        await Assert.That(body[8]).IsEqualTo((byte)0x4A);
    }

    [Test]
    public async Task GateOff_SetsFlagEightAndAppendsAFalseByte()
    {
        var body = Bytes(new UnitReqsValidationResult(SkillResultKeys.skill_urk_level, 0, 0) { DisplayMessage = false });

        await Assert.That(body.Length).IsEqualTo(10);
        await Assert.That(body[7]).IsEqualTo((byte)0x09);
        await Assert.That(body[8]).IsEqualTo((byte)0x4A);
        await Assert.That(body[9]).IsEqualTo((byte)0x00);
    }

    [Test]
    public async Task UShortDetail_IsWrittenAfterTheResultByte()
    {
        var body = Bytes(new UnitReqsValidationResult(SkillResultKeys.skill_urk_expedition_member, 0x328, 0));

        await Assert.That(body.Length).IsEqualTo(11);
        await Assert.That(body[7]).IsEqualTo((byte)0x03);
        await Assert.That(body[8]).IsEqualTo((byte)SkillResult.UrkExpeditionMember);
        await Assert.That(BitConverter.ToUInt16(body, 9)).IsEqualTo((ushort)0x328);
    }

    [Test]
    public async Task UIntDetail_IsWrittenWithFlagFour()
    {
        var body = Bytes(new UnitReqsValidationResult(SkillResultKeys.skill_urk_buff, 0, 25817));

        await Assert.That(body.Length).IsEqualTo(13);
        await Assert.That(body[7]).IsEqualTo((byte)0x05);
        await Assert.That(body[8]).IsEqualTo((byte)SkillResult.UrkBuff);
        await Assert.That(BitConverter.ToUInt32(body, 9)).IsEqualTo(25817u);
    }

    [Test]
    public async Task EveryField_InOrderWhenAllAreSet()
    {
        var body = Bytes(new UnitReqsValidationResult(SkillResultKeys.skill_urk_hero, 0x356, 2) { DisplayMessage = false });

        await Assert.That(body.Length).IsEqualTo(16);
        await Assert.That(body[7]).IsEqualTo((byte)0x0F);
        await Assert.That(body[8]).IsEqualTo((byte)SkillResult.UrkHero);
        await Assert.That(BitConverter.ToUInt16(body, 9)).IsEqualTo((ushort)0x356);
        await Assert.That(BitConverter.ToUInt32(body, 11)).IsEqualTo(2u);
        await Assert.That(body[15]).IsEqualTo((byte)0x00);
    }

    [Test]
    public async Task NativeByte_WinsOverTheKeyMapping()
    {
        var result = new UnitReqsValidationResult(SkillResultKeys.skill_failure, 0, 0)
        {
            NativeResult = SkillResult.UrkDominionMember
        };

        var body = Bytes(result);

        await Assert.That(body[7]).IsEqualTo((byte)0x01);
        await Assert.That(body[8]).IsEqualTo((byte)0x9C);
    }

    [Test]
    public async Task OrGroupFailure_IsTheSingleByte0x31()
    {
        var body = Bytes(new UnitReqsValidationResult(SkillResultKeys.skill_unit_reqs_or_fail, 0, 0));

        await Assert.That(body.Length).IsEqualTo(9);
        await Assert.That(body[7]).IsEqualTo((byte)0x01);
        await Assert.That(body[8]).IsEqualTo((byte)0x31);
    }

    [Test]
    public async Task Success_IsAnEmptyTail()
    {
        var body = Bytes(new UnitReqsValidationResult(SkillResultKeys.ok, 0, 0));

        await Assert.That(body.Length).IsEqualTo(8);
        await Assert.That(body[7]).IsEqualTo((byte)0x00);
    }
}
