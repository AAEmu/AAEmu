using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Features;

namespace AAEmu.UnitTests.Game.Models.Game.Features;

/// <summary>
/// Pins the 10.0.2.13 fset wire contract.
/// </summary>
public class FeatureSetTests
{
    [Test]
    public async Task Write_EmitsU16LengthPrefixAnd31Bytes()
    {
        var stream = new PacketStream();
        new FeatureSet().Write(stream);
        stream.Rollback();

        var length = stream.ReadUInt16();
        await Assert.That(length).IsEqualTo((ushort)FeatureSet.FsetLength);
        await Assert.That(stream.ReadBytes(length).Length).IsEqualTo(FeatureSet.FsetLength);
    }

    [Test]
    [Arguments(Feature.siege, 0, 0x01)]
    [Arguments(Feature.premium, 0, 0x10)]
    [Arguments(Feature.ranking, 4, 0x10)]
    [Arguments(Feature.ingamecashshop, 4, 0x40)]
    [Arguments(Feature.customsaveload, 5, 0x01)]
    [Arguments(Feature.bm_mileage, 5, 0x08)]
    [Arguments(Feature.slave_customize, 6, 0x01)]
    [Arguments(Feature.sensitiveOpeartion, 7, 0x01)]
    [Arguments(Feature.mailCoolTime, 9, 0x08)]
    [Arguments(Feature.auctionPostBuff, 11, 0x08)]
    [Arguments(Feature.houseTaxPrepay, 11, 0x80)]
    [Arguments(Feature.heirLevel, 12, 0x20)]
    [Arguments(Feature.useHeirSkill, 25, 0x04)]
    [Arguments(Feature.useCosplayLooksSlot, 27, 0x10)]
    [Arguments(Feature.notGainLeaderShipPoint, 30, 0x04)]
    public async Task Set_LandsOnTheByteAndBitTheClientReads(Feature feature, int byteIndex, int mask)
    {
        var blob = WriteAndGetBlob(f => f.Set(feature, true));

        await Assert.That(blob[byteIndex] & mask).IsEqualTo(mask);
        for (var i = 0; i < FeatureSet.FsetLength; i++)
        {
            var expected = i == byteIndex ? mask : 0;
            await Assert.That((int)blob[i]).IsEqualTo(expected);
        }
    }

    [Test]
    public async Task ScalarBytes_AreWrittenAsNumbers()
    {
        var blob = WriteAndGetBlob(f =>
        {
            f.PlayerLevelLimit = 55;
            f.MateLevelLimit = 50;
            f.UnknownTimeLimit = 7;
            f.ButlerLevelLimit = 30;
        });

        await Assert.That(blob[1]).IsEqualTo((byte)55);
        await Assert.That(blob[8]).IsEqualTo((byte)50);
        await Assert.That(blob[10]).IsEqualTo((byte)7);
        await Assert.That(blob[26]).IsEqualTo((byte)30);
    }

    [Test]
    [Arguments(1)]
    [Arguments(8)]
    [Arguments(10)]
    [Arguments(26)]
    public async Task Set_RefusesToTouchScalarBytes(int scalarByteIndex)
    {
        var fset = new FeatureSet { PlayerLevelLimit = 55, MateLevelLimit = 50, ButlerLevelLimit = 30 };

        for (var bit = 0; bit < 8; bit++)
        {
            var feature = (Feature)(scalarByteIndex * 8 + bit);
            await Assert.That(FeatureSet.IsValid(feature)).IsFalse();
            await Assert.That(fset.Set(feature, true)).IsFalse();
        }

        var blob = GetBlob(fset);
        await Assert.That(blob[1]).IsEqualTo((byte)55);
        await Assert.That(blob[8]).IsEqualTo((byte)50);
        await Assert.That(blob[26]).IsEqualTo((byte)30);
    }

    [Test]
    public async Task Set_RefusesIdsPastTheEndOfTheBlob()
    {
        var fset = new FeatureSet();
        await Assert.That(fset.Set((Feature)(FeatureSet.FsetLength * 8), true)).IsFalse();
        await Assert.That(fset.Set((Feature)(-1), true)).IsFalse();
    }

    [Test]
    public async Task EveryDeclaredFeature_IsAddressable()
    {
        // No Feature may sit in a scalar byte or past the end of the blob.
        foreach (var feature in Enum.GetValues<Feature>())
            await Assert.That(FeatureSet.IsValid(feature)).IsTrue();
    }

    [Test]
    public async Task CheckRoundTripsSet()
    {
        var fset = new FeatureSet();
        await Assert.That(fset.Check(Feature.useCraftOrder)).IsFalse();

        fset.Set(Feature.useCraftOrder, true);
        await Assert.That(fset.Check(Feature.useCraftOrder)).IsTrue();

        fset.Set(Feature.useCraftOrder, false);
        await Assert.That(fset.Check(Feature.useCraftOrder)).IsFalse();
    }

    private static byte[] WriteAndGetBlob(Action<FeatureSet> configure)
    {
        var fset = new FeatureSet();
        configure(fset);
        return GetBlob(fset);
    }

    private static byte[] GetBlob(FeatureSet fset)
    {
        var stream = new PacketStream();
        fset.Write(stream);
        stream.Rollback();
        return stream.ReadBytes(stream.ReadUInt16());
    }
}
