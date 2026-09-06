using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;

namespace AAEmu.UnitTests.Game.Models.Game;

public class AccountAttributeConfigRulesTests
{
    [Test]
    public async Task AuctionPost_IsAdvertised_WithTheOtherLiveKinds()
    {
        await Assert.That(AccountAttributeConfigRules.KindIsUsed((byte)AccountAttributeKind.AuctionPost))
            .IsTrue();
        await Assert.That(AccountAttributeConfigRules.KindIsUsed((byte)AccountAttributeKind.AccountBuff))
            .IsTrue();
        await Assert.That(AccountAttributeConfigRules.KindIsUsed((byte)AccountAttributeKind.Ulc))
            .IsTrue();
        await Assert.That(AccountAttributeConfigRules.KindIsUsed(0)).IsFalse();
    }

    [Test]
    public async Task ConfigPacket_WritesFourUsedBytesInKindOrder()
    {
        var body = new SCAccountAttributeConfigPacket()
            .Write(new PacketStream())
            .GetBytes();

        await Assert.That(body).IsEquivalentTo(new byte[] { 0, 1, 1, 1 });
    }

    [Test]
    public async Task ListingGrant_AddsExtraKindZeroOnce()
    {
        var attributes = new List<AccountAttribute>();
        AccountAttributeGrantRules.EnsureListingGrant(attributes, 39);
        AccountAttributeGrantRules.EnsureListingGrant(attributes, 39);

        await Assert.That(attributes).HasCount().EqualTo(1);
        await Assert.That(attributes[0].KindId).IsEqualTo((uint)AccountAttributeKind.AuctionPost);
        await Assert.That(attributes[0].KindValue).IsEqualTo(AccountAttributeGrantRules.ListingExtraKind);
        await Assert.That(attributes[0].Count).IsEqualTo(1);
        await Assert.That(attributes[0].Starts).IsEqualTo(DateTime.UnixEpoch);
        await Assert.That(attributes[0].Expires).IsEqualTo(DateTime.UnixEpoch);
    }

    [Test]
    public async Task ListingGrant_KeepsAnExistingExtraKindZeroRow()
    {
        var existing = AccountAttributeGrantRules.CreateListingGrant(39);
        existing.Count = 4;
        var attributes = new List<AccountAttribute> { existing };

        AccountAttributeGrantRules.EnsureListingGrant(attributes, 39);

        await Assert.That(attributes).HasCount().EqualTo(1);
        await Assert.That(attributes[0].Count).IsEqualTo(4);
    }

    [Test]
    public async Task ListingGrant_ListPacket_WritesKindExtraKindCountAndZeroDates()
    {
        var grant = AccountAttributeGrantRules.CreateListingGrant(39);
        var body = new SCAccountAttributeListPacket([grant])
            .Write(new PacketStream())
            .GetBytes();

        var expected = new PacketStream()
            .Write((uint)1)
            .Write((byte)AccountAttributeKind.AuctionPost)
            .Write(AccountAttributeGrantRules.ListingExtraKind)
            .Write((byte)0)
            .Write((uint)1)
            .Write(DateTime.UnixEpoch)
            .Write(DateTime.UnixEpoch)
            .GetBytes();

        await Assert.That(body).IsEquivalentTo(expected);
    }
}
