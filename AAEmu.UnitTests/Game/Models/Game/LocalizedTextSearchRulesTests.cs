using AAEmu.Game.Models.Game;

namespace AAEmu.UnitTests.Game.Models.Game;

public class LocalizedTextSearchRulesTests
{
    [Test]
    public async Task LanguageColumns_KeepsLocaleCellsAndDropsTheRowKey()
    {
        var columns = LocalizedTextSearchRules.LanguageColumns(
        [
            "id", "tbl_name", "tbl_column_name", "idx",
            "ko", "en_us", "zh_cn", "ja", "ru", "zh_tw",
            "de", "fr", "th", "ind", "en_sg", "pt", "es",
            "id"
        ]);

        await Assert.That(columns).IsEquivalentTo([
            "ko", "en_us", "zh_cn", "ja", "ru", "zh_tw",
            "de", "fr", "th", "ind", "en_sg", "pt", "es"
        ]);
        await Assert.That(LocalizedTextSearchRules.IsKeyColumn("id")).IsTrue();
        await Assert.That(LocalizedTextSearchRules.IsKeyColumn("ko")).IsFalse();
        await Assert.That(LocalizedTextSearchRules.LanguageColumns(null)).IsEmpty();
        await Assert.That(LocalizedTextSearchRules.LanguageColumns(["", "idx", null])).IsEmpty();
    }

    [Test]
    public async Task UniqueNames_SkipsEmptyDuplicateAndWhitespaceCells()
    {
        var names = LocalizedTextSearchRules.UniqueNames(
        [
            "전문화 확장의 인장",
            "",
            "  ",
            null,
            "Specialization Snowflake",
            "Specialization Snowflake",
            "博学之章"
        ]);

        await Assert.That(names).IsEquivalentTo([
            "전문화 확장의 인장",
            "Specialization Snowflake",
            "博学之章"
        ]);
        await Assert.That(LocalizedTextSearchRules.UniqueNames(null)).IsEmpty();
    }

    [Test]
    public async Task BuildSearchString_JoinsEveryLoadedLanguage()
    {
        var haystack = LocalizedTextSearchRules.BuildSearchString(
            "전문화 확장의 인장",
            ["Specialization Snowflake", "", "博学之章"]);

        await Assert.That(haystack.Contains("전문화 확장의 인장", StringComparison.Ordinal)).IsTrue();
        await Assert.That(haystack.Contains("specialization snowflake", StringComparison.Ordinal)).IsTrue();
        await Assert.That(haystack.Contains("博学之章", StringComparison.Ordinal)).IsTrue();
        await Assert.That(LocalizedTextSearchRules.BuildSearchString("iron ore", null)).IsEqualTo("iron ore");
    }
}
