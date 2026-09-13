using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public sealed class SCExpeditionDescPacketTests
{
    [Test]
    public async Task DescriptorCarriesTheRecipientMembersOwnContributionBalance()
    {
        var expedition = new Expedition
        {
            Id = (FactionsEnum)41,
            MotherId = (FactionsEnum)101,
            Name = "Guild",
            OwnerName = "Owner",
            Notice = string.Empty
        };

        var owner = Write(expedition, 1200);
        var member = Write(expedition, 75);

        await Assert.That(ReadContribution(owner)).IsEqualTo(1200L);
        await Assert.That(ReadContribution(member)).IsEqualTo(75L);
    }

    [Test]
    public async Task DescriptorIsAnImmutableSnapshotOfGuildState()
    {
        var expedition = new Expedition
        {
            Id = (FactionsEnum)41, MotherId = (FactionsEnum)101, Name = "Before",
            OwnerName = "Owner", Notice = "Before notice", Level = 2, Exp = 10
        };
        var packet = new SCExpeditionDescPacket(expedition, 75);

        expedition.Name = "After";
        expedition.Notice = "After notice";
        expedition.Level = 3;
        expedition.Exp = 99;

        var stream = packet.Write(new PacketStream());
        stream.Rollback();
        _ = stream.ReadUInt32();
        _ = stream.ReadUInt32();
        await Assert.That(stream.ReadString()).IsEqualTo("Before");
        _ = stream.ReadUInt64();
        _ = stream.ReadString();
        _ = stream.ReadSByte();
        _ = stream.ReadByte();
        _ = stream.ReadDateTime();
        _ = stream.ReadBoolean();
        _ = stream.ReadBoolean();
        _ = stream.ReadByte();
        _ = stream.ReadDateTime();
        _ = stream.ReadBoolean();
        await Assert.That(stream.ReadInt32()).IsEqualTo(2);
        await Assert.That(stream.ReadInt32()).IsEqualTo(10);
    }

    [Test]
    public void DescriptorBroadcastSkipsOfflineRosterEntries()
    {
        var expedition = new Expedition { Id = (FactionsEnum)41, Name = "Guild", OwnerName = "Owner" };
        expedition.Members.Add(new ExpeditionMember
            { ExpeditionId = expedition.Id, CharacterId = 7, Name = "Offline", ContributionPoint = 99 });
        var world = Mock.Of<IWorldManager>();
        world.GetCharacterById(7).Returns((AAEmu.Game.Models.Game.Char.Character)null);

        expedition.SendDescriptor(world.Object);
    }

    private static PacketStream Write(Expedition expedition, uint contribution)
    {
        var stream = new SCExpeditionDescPacket(expedition, contribution).Write(new PacketStream());
        stream.Rollback();
        return stream;
    }

    private static long ReadContribution(PacketStream stream)
    {
        _ = stream.ReadUInt32(); // id
        _ = stream.ReadUInt32(); // mother
        _ = stream.ReadString();
        _ = stream.ReadUInt64();
        _ = stream.ReadString();
        _ = stream.ReadSByte();
        _ = stream.ReadByte();
        _ = stream.ReadDateTime();
        _ = stream.ReadBoolean();
        _ = stream.ReadBoolean();
        _ = stream.ReadByte();
        _ = stream.ReadDateTime();
        _ = stream.ReadBoolean();
        _ = stream.ReadInt32(); // level
        _ = stream.ReadInt32(); // exp
        _ = stream.ReadDateTime();
        _ = stream.ReadUInt32();
        _ = stream.ReadUInt32();
        _ = stream.ReadDateTime();
        _ = stream.ReadInt16();
        _ = stream.ReadString();
        _ = stream.ReadUInt32();
        _ = stream.ReadUInt32();
        _ = stream.ReadUInt32();
        _ = stream.ReadInt32();
        _ = stream.ReadInt64();
        return stream.ReadInt64();
    }
}
