using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Expeditions;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SCFamilyPacketWireTests
{
    [Test]
    public async Task FamilyDesc_WritesCompleteNativeDescriptor()
    {
        var family = CreateFamily();
        var stream = new SCFamilyDescPacket(family).Write(new PacketStream());

        stream.Rollback();
        await AssertFamily(stream, family);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task FamilyMemberAdded_WritesDescriptorThenUnsignedIndex()
    {
        var family = CreateFamily();
        var stream = new SCFamilyMemberAddedPacket(family, uint.MaxValue).Write(new PacketStream());

        stream.Rollback();
        await AssertFamily(stream, family);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(uint.MaxValue);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task FamilyInfoSet_IncludesNoticeInNativeOrder()
    {
        var stream = new SCFamilyInfoSetPacket(17, 3, 456, "The Family", "Meet at dawn", 2, 4, 9876543210)
            .Write(new PacketStream());

        stream.Rollback();
        await Assert.That(stream.ReadInt32()).IsEqualTo(17);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(3u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(456u);
        await Assert.That(stream.ReadString()).IsEqualTo("The Family");
        await Assert.That(stream.ReadString()).IsEqualTo("Meet at dawn");
        await Assert.That(stream.ReadInt32()).IsEqualTo(2);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(4u);
        await Assert.That(stream.ReadInt64()).IsEqualTo(9876543210);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task FamilyInvitation_WritesInvitorAsNativeUInt64()
    {
        var stream = new SCFamilyInvitationPacket(uint.MaxValue, "Inviter", 31, "Guardian")
            .Write(new PacketStream());

        stream.Rollback();
        await Assert.That(stream.ReadUInt64()).IsEqualTo(uint.MaxValue);
        await Assert.That(stream.ReadString()).IsEqualTo("Inviter");
        await Assert.That(stream.ReadInt32()).IsEqualTo(31);
        await Assert.That(stream.ReadString()).IsEqualTo("Guardian");
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task FamilyMemberUpdates_WriteSignedFamilyAndUInt64MemberIds()
    {
        var removed = new SCFamilyMemberRemovedPacket(uint.MaxValue, true, uint.MaxValue).Write(new PacketStream());
        removed.Rollback();
        await Assert.That(removed.ReadInt32()).IsEqualTo(-1);
        await Assert.That(removed.ReadUInt64()).IsEqualTo(uint.MaxValue);
        await Assert.That(removed.ReadBoolean()).IsTrue();
        await Assert.That(removed.LeftBytes).IsEqualTo(0);

        var title = new SCFamilyTitleChangedPacket(uint.MaxValue, uint.MaxValue, "Scout").Write(new PacketStream());
        title.Rollback();
        await Assert.That(title.ReadInt32()).IsEqualTo(-1);
        await Assert.That(title.ReadUInt64()).IsEqualTo(uint.MaxValue);
        await Assert.That(title.ReadString()).IsEqualTo("Scout");
        await Assert.That(title.LeftBytes).IsEqualTo(0);

        var name = new SCFamilyMemberNameChangedPacket(uint.MaxValue, uint.MaxValue, "Renamed").Write(new PacketStream());
        name.Rollback();
        await Assert.That(name.ReadInt32()).IsEqualTo(-1);
        await Assert.That(name.ReadUInt64()).IsEqualTo(uint.MaxValue);
        await Assert.That(name.ReadString()).IsEqualTo("Renamed");
        await Assert.That(name.LeftBytes).IsEqualTo(0);

        var level = new SCFamilyChangeMemberLevelPacket(-1, uint.MaxValue, 55, 8).Write(new PacketStream());
        level.Rollback();
        await Assert.That(level.ReadInt32()).IsEqualTo(-1);
        await Assert.That(level.ReadUInt64()).IsEqualTo(uint.MaxValue);
        await Assert.That(level.ReadSByte()).IsEqualTo((sbyte)55);
        await Assert.That(level.ReadSByte()).IsEqualTo((sbyte)8);
        await Assert.That(level.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task ExpeditionLists_RejectMoreThanNativeTwentyEntries()
    {
        var expeditions = Enumerable.Range(0, 21).Select(_ => new Expedition()).ToList();
        var policies = Enumerable.Range(0, 21).Select(_ => new ExpeditionRolePolicy()).ToList();

        await Assert.That(() => new SCExpeditionListPacket(expeditions).Write(new PacketStream()))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new SCExpeditionRolePolicyListPacket(policies).Write(new PacketStream()))
            .Throws<ArgumentOutOfRangeException>();
    }

    private static Family CreateFamily()
    {
        var family = new Family
        {
            Id = 42,
            Name = "Home Team",
            Notice = "Welcome home",
            Level = 3,
            Exp = 4567,
            IncreasedMemberCount = 2,
            ResetTime = 111222333444,
            ChangeNameTime = 555666777888
        };
        family.AddMember(new FamilyMember
        {
            Id = uint.MaxValue,
            Name = "Offline Member",
            Level = 55,
            HeirLevel = 7,
            Role = 3,
            Title = "Pathfinder",
            RoleUpdateTime = 999888777666
        });
        family.ActSanctions[4] = 1234567890123;
        return family;
    }

    private static async Task AssertFamily(PacketStream stream, Family family)
    {
        var member = family.Members[0];
        await Assert.That(stream.ReadInt32()).IsEqualTo((int)family.Id);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(1u);
        await Assert.That(stream.ReadUInt64()).IsEqualTo(member.Id);
        await Assert.That(stream.ReadString()).IsEqualTo(member.Name);
        await Assert.That(stream.ReadByte()).IsEqualTo(member.Level);
        await Assert.That(stream.ReadByte()).IsEqualTo(member.HeirLevel);
        await Assert.That(stream.ReadByte()).IsEqualTo(member.Role);
        await Assert.That(stream.ReadBoolean()).IsFalse();
        await Assert.That(stream.ReadString()).IsEqualTo(member.Title);
        await Assert.That(stream.ReadInt64()).IsEqualTo(member.RoleUpdateTime);
        await Assert.That(stream.ReadString()).IsEqualTo(family.Name);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(family.Level);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(family.Exp);
        await Assert.That(stream.ReadString()).IsEqualTo(family.Notice);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(family.IncreasedMemberCount);
        await Assert.That(stream.ReadInt64()).IsEqualTo(family.ResetTime);
        await Assert.That(stream.ReadInt64()).IsEqualTo(family.ChangeNameTime);
        await Assert.That(stream.ReadInt32()).IsEqualTo(1);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)4);
        await Assert.That(stream.ReadInt64()).IsEqualTo(1234567890123);
    }
}
