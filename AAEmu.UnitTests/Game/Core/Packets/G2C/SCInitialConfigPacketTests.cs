using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

[NotInParallel]
public class SCInitialConfigPacketTests
{
    private IServiceProvider _previousProvider;
    private ServiceProvider _testProvider;

    [Before(Test)]
    public void SetUpConfiguration()
    {
        _previousProvider = SingletonContainer.ServiceProvider;
        _testProvider = new ServiceCollection()
            .AddSingleton<IOptions<AppConfiguration>>(Options.Create(new AppConfiguration
            {
                Labor = new CurrencyValuesConfig()
            }))
            .BuildServiceProvider();
        SingletonContainer.ServiceProvider = _testProvider;
    }

    [After(Test)]
    public void RestoreConfiguration()
    {
        SingletonContainer.ServiceProvider = _previousProvider;
        _testProvider.Dispose();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Write_SnapshotsAll31FeatureBytesAndOverridesOnlySnow(bool isSnowing)
    {
        var source = CreateFeatures(!isSnowing);
        var original = GetBlob(source);
        var expected = original.ToArray();
        // The client's 10.0.2.13 handlers consume snow at fset byte 7, mask 0x04.
        SetBit(expected, 7, 0x04, isSnowing);
        var packet = new SCInitialConfigPacket(source, isSnowing);

        var stream = packet.Write(new PacketStream());
        stream.Rollback();

        await Assert.That(stream.ReadString()).IsEqualTo(AppConfiguration.Instance.InitialConfig.Host);
        var length = stream.ReadUInt16();
        var actual = stream.ReadBytes(length);
        await Assert.That(length).IsEqualTo((ushort)FeatureSet.FsetLength);
        await Assert.That(actual.SequenceEqual(expected)).IsTrue();
        await Assert.That(GetBlob(source).SequenceEqual(original)).IsTrue();
        await Assert.That(source.Check(Feature.fset_7_2_unknown)).IsEqualTo(!isSnowing);
    }

    private static FeatureSet CreateFeatures(bool snowEnabled)
    {
        var features = new FeatureSet
        {
            PlayerLevelLimit = 55,
            MateLevelLimit = 50,
            UnknownTimeLimit = 7,
            ButlerLevelLimit = 30
        };
        features.Set(Feature.siege, true);
        features.Set(Feature.notGainLeaderShipPoint, true);
        features.Set(Feature.fset_7_2_unknown, snowEnabled);
        return features;
    }

    private static byte[] GetBlob(FeatureSet features)
    {
        var stream = new PacketStream();
        features.Write(stream);
        stream.Rollback();
        return stream.ReadBytes(stream.ReadUInt16());
    }

    private static void SetBit(byte[] bytes, int byteIndex, byte mask, bool enabled)
    {
        bytes[byteIndex] = enabled
            ? (byte)(bytes[byteIndex] | mask)
            : (byte)(bytes[byteIndex] & ~mask);
    }
}
