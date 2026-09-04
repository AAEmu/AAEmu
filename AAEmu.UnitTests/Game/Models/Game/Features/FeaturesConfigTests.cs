using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Features;
using Microsoft.Extensions.Configuration;

namespace AAEmu.UnitTests.Game.Models.Game.Features;

/// <summary>
/// Guards the shipped <c>Configurations/Features.json</c>. The fset baseline lives in that file, so a
/// key the <see cref="Feature"/> enum does not define turns a feature off with nothing but a log line
/// to show for it.
/// </summary>
public class FeaturesConfigTests
{
    private static FeaturesConfig LoadShippedConfig()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Configurations", "Features.json");
        return new ConfigurationBuilder()
            .AddJsonFile(path, optional: false)
            .Build()
            .GetSection("Features")
            .Get<FeaturesConfig>()!;
    }

    [Test]
    public async Task ShippedConfig_Binds()
    {
        var config = LoadShippedConfig();

        await Assert.That(config).IsNotNull();
        await Assert.That(config.Flags.Count > 0).IsTrue();
    }

    [Test]
    public async Task EveryConfiguredFlag_NamesAnAddressableFeature()
    {
        var config = LoadShippedConfig();
        var fset = new FeatureSet();
        var rejected = new List<string>();

        foreach (var (name, enabled) in config.Flags)
        {
            if (!Enum.TryParse<Feature>(name, true, out var feature) || !Enum.IsDefined(feature))
                rejected.Add($"{name} (undefined)");
            else if (!fset.Set(feature, enabled))
                rejected.Add($"{name} (bit {(int)feature} is unaddressable)");
        }

        await Assert.That(string.Join(", ", rejected)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ConfiguredFlags_ProduceTheExpectedBlob()
    {
        // Pins the shipped baseline byte-for-byte. A change here changes what the client is told this
        // server supports, so it should be a deliberate edit rather than a side effect. The scalar bytes
        // (1, 8, 10, 26) stay zero: FeaturesManager fills those from the level caps, not from Flags.
        var config = LoadShippedConfig();
        var fset = new FeatureSet();
        foreach (var (name, enabled) in config.Flags)
            fset.Set(Enum.Parse<Feature>(name, true), enabled);

        await Assert.That(fset.ToString()).IsEqualTo(
            // All bits on except fset_7_2_unknown (snow), restriction/moderation/dev-security switches,
            // and scalar bytes 1/8/10/26 (FeaturesManager fills those from level caps at boot).
            "57 00 00 00 f4 9f 61 02 00 4e 00 fe bf cf 2d 00 00 ff bf " +
            "f5 7f 9e b7 00 ec bf 00 d0 79 f2 02");
    }
}
