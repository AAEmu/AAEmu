using AAEmu.Game.Core.Managers;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class LocalizationManagerTests
{
    [Test]
    public async Task Get_UsesTheDisplayLanguageAndFallsBackWhenThatCellIsEmpty()
    {
        var loc = new LocalizationManager();
        loc.AddTranslation("items", "name", 29656, "");
        loc.AddTranslations("items", "name", 29656, ["전문화 확장의 인장", "", "博学之章"]);

        await Assert.That(loc.Get("items", "name", 29656, "전문화 확장의 인장")).IsEqualTo("전문화 확장의 인장");
        await Assert.That(loc.Get("items", "name", 1, "missing")).IsEqualTo("missing");
    }

    [Test]
    public async Task GetAll_ReturnsEveryNonEmptyLanguageCell()
    {
        var loc = new LocalizationManager();
        loc.AddTranslation("items", "name", 29656, "Specialization Snowflake");
        loc.AddTranslations("items", "name", 29656,
        [
            "전문화 확장의 인장",
            "Specialization Snowflake",
            "博学之章",
            ""
        ]);

        var names = loc.GetAll("items", "name", 29656);
        await Assert.That(names).IsEquivalentTo([
            "Specialization Snowflake",
            "전문화 확장의 인장",
            "博学之章"
        ]);
        await Assert.That(loc.GetAll("items", "name", 1)).IsEmpty();
    }
}
