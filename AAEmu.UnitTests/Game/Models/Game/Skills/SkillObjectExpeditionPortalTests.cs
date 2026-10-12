using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public sealed class SkillObjectExpeditionPortalTests
{
    [Test]
    public async Task Read_PortalIdThenBc_LeavesInputDirectionByte()
    {
        // flag already consumed by CSStartSkill; payload is portalId u32 + bc (3), then inputDirection.
        var stream = new PacketStream(
        [
            0x01, 0x00, 0x00, 0x00, // portalId = 1
            0xA1, 0x8A, 0x01,       // ObjId bc = 101025
            0x00                    // inputDirection
        ]);

        var skillObject = new SkillObjectExpeditionPortal();
        skillObject.Read(stream);

        await Assert.That(skillObject.PortalId).IsEqualTo(1u);
        await Assert.That(skillObject.ObjId).IsEqualTo(101025u);
        await Assert.That(stream.LeftBytes).IsEqualTo(1);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0);
    }

    [Test]
    public async Task ExpeditionPortal_IsAShapeTheServerReads()
    {
        await Assert.That(SkillObject.IsKnownType((int)SkillObjectType.ExpeditionPortal)).IsTrue();
    }

    [Test]
    public async Task WriteSkillCastExtra_EchoesPortalIdAndBc()
    {
        var stream = new PacketStream();
        stream.WriteSkillCastExtra(new SkillObjectExpeditionPortal
        {
            Flag = SkillObjectType.ExpeditionPortal,
            PortalId = 1,
            ObjId = 101025
        });
        var body = stream.GetBytes();

        // flag + portalId u32 + bc(3) + inputDirection
        await Assert.That(body.Length).IsEqualTo(1 + 4 + 3 + 1);
        await Assert.That(body[0]).IsEqualTo((byte)SkillObjectType.ExpeditionPortal);
        await Assert.That(BitConverter.ToUInt32(body, 1)).IsEqualTo(1u);
        await Assert.That(body[5] | (body[6] << 8) | (body[7] << 16)).IsEqualTo(101025);
        await Assert.That(body[8]).IsEqualTo((byte)0);
    }
}
