using AAEmu.Game.Models.Game.Indun;

namespace AAEmu.UnitTests.Game.Models.Game.Indun;

/// <summary>
/// The <c>indun_zones.option</c> parser. Samples are the shipped column values (zone group ids cited).
/// </summary>
public class IndunZoneOptionTests
{
    [Test]
    public async Task Parse_Zone130_HereafterRebellion()
    {
        var option = IndunZoneOption.Parse("{\"tower_def\":127,\"ready_time\":10,\"play_time\":1000,\"end_time\":60}");

        await Assert.That(option.TowerDefId).IsEqualTo(127u);
        await Assert.That(option.ReadySeconds).IsEqualTo(10);
        await Assert.That(option.PlaySeconds).IsEqualTo(1000);
        await Assert.That(option.EndSeconds).IsEqualTo(60);
        await Assert.That(option.IsScripted).IsTrue();
    }

    [Test]
    public async Task Parse_Zone58_TowerDefAndLongerPlay()
    {
        var option = IndunZoneOption.Parse("{\"tower_def\":178,\"ready_time\":20,\"play_time\":3600,\"end_time\":600}");

        await Assert.That(option.TowerDefId).IsEqualTo(178u);
        await Assert.That(option.PlaySeconds).IsEqualTo(3600);
    }

    [Test]
    public async Task Parse_Zone126_IgnoresExtraSiblingKeys()
    {
        var option = IndunZoneOption.Parse(
            "{\"tower_def\":119,\"ready_time\":10,\"play_time\":3600,\"end_time\":60,\"faction\":198,\"min_corps_size\":3}");

        await Assert.That(option.TowerDefId).IsEqualTo(119u);
        await Assert.That(option.ReadySeconds).IsEqualTo(10);
        await Assert.That(option.EndSeconds).IsEqualTo(60);
    }

    [Test]
    public async Task Parse_WithSpacesAroundKeysAndValues()
    {
        var option = IndunZoneOption.Parse(
            "{ \"matching\" : \"local\", \"tower_def\" : 0, \"ready_time\" : 0, \"play_time\" : 0, \"end_time\" : 0 }");

        await Assert.That(option).IsEqualTo(IndunZoneOption.None);
        await Assert.That(option.IsScripted).IsFalse();
    }

    [Test]
    public async Task Parse_Zone114_HasBudgetWithoutTowerDef()
    {
        // tower_def 0 but a real ready/play/end budget: still a scripted clock.
        var option = IndunZoneOption.Parse("{\"tower_def\":0,\"ready_time\":10,\"play_time\":1000,\"end_time\":60}");

        await Assert.That(option.TowerDefId).IsEqualTo(0u);
        await Assert.That(option.IsScripted).IsTrue();
    }

    [Test]
    public async Task Parse_MissingKeyDefaultsToZero()
    {
        var option = IndunZoneOption.Parse("{\"tower_def\":127,\"play_time\":100}");

        await Assert.That(option.TowerDefId).IsEqualTo(127u);
        await Assert.That(option.ReadySeconds).IsEqualTo(0);
        await Assert.That(option.EndSeconds).IsEqualTo(0);
    }

    [Test]
    public async Task Parse_BlankOrMalformed_IsNone()
    {
        await Assert.That(IndunZoneOption.Parse(null)).IsEqualTo(IndunZoneOption.None);
        await Assert.That(IndunZoneOption.Parse("")).IsEqualTo(IndunZoneOption.None);
        await Assert.That(IndunZoneOption.Parse("   ")).IsEqualTo(IndunZoneOption.None);
        await Assert.That(IndunZoneOption.Parse("{\"tower_def\":")).IsEqualTo(IndunZoneOption.None);
        await Assert.That(IndunZoneOption.Parse("not json")).IsEqualTo(IndunZoneOption.None);
        await Assert.That(IndunZoneOption.Parse("[1,2,3]")).IsEqualTo(IndunZoneOption.None);
    }
}
