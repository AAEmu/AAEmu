using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

public class QuestAcceptLevelRangeRulesTests
{
    // quest_act_con_accept_level_ranges 2: festival quest 10930, level_min 10, level_max 19.
    [Test]
    public async Task Quest10930_PassesInsideTenToNineteen()
    {
        await Assert.That(QuestAcceptLevelRangeRules.InRange(10, 10, 19)).IsTrue();
        await Assert.That(QuestAcceptLevelRangeRules.InRange(15, 10, 19)).IsTrue();
        await Assert.That(QuestAcceptLevelRangeRules.InRange(19, 10, 19)).IsTrue();
    }

    [Test]
    public async Task Quest10930_FailsOutsideTenToNineteen()
    {
        await Assert.That(QuestAcceptLevelRangeRules.InRange(9, 10, 19)).IsFalse();
        await Assert.That(QuestAcceptLevelRangeRules.InRange(20, 10, 19)).IsFalse();
        await Assert.That(QuestAcceptLevelRangeRules.InRange(55, 10, 19)).IsFalse();
    }

    // Rows 16 and 24: anniversary quests 10534 (10..125) and 10542 (54..125), whose quest_contexts
    // min_level and max_level are 0.
    [Test]
    public async Task AnniversaryChain_GatesOnLevelMinOnly()
    {
        await Assert.That(QuestAcceptLevelRangeRules.InRange(10, 10, 125)).IsTrue();
        await Assert.That(QuestAcceptLevelRangeRules.InRange(125, 10, 125)).IsTrue();
        await Assert.That(QuestAcceptLevelRangeRules.InRange(9, 10, 125)).IsFalse();
        await Assert.That(QuestAcceptLevelRangeRules.InRange(53, 54, 125)).IsFalse();
        await Assert.That(QuestAcceptLevelRangeRules.InRange(54, 54, 125)).IsTrue();
    }

    // Quest 10930, Start component 47533: the range act is the component's only act, so a level
    // outside 10..19 has to refuse the accept. RunCurrentStep's false was dropped, and the quest
    // stayed in the journal at Start.
    [Test]
    public async Task Quest10930_RefusesTheAcceptOutsideItsRange()
    {
        var template = StartComponent(10930, 47533, out var component);
        component.ActTemplates.Add(new QuestActConAcceptLevelRange(component) { DetailId = 2, LevelMin = 10, LevelMax = 19 });

        await Assert.That(QuestAcceptLevelRangeRules.RefusesAccept(template, 15)).IsFalse();
        await Assert.That(QuestAcceptLevelRangeRules.RefusesAccept(template, 9)).IsTrue();
        await Assert.That(QuestAcceptLevelRangeRules.RefusesAccept(template, 20)).IsTrue();
        await Assert.That(QuestAcceptLevelRangeRules.RefusesAccept(template, 55)).IsTrue();
    }

    // Quest 10542, Start component 45935: quests 10534 to 10542 share their Start component with a
    // QuestActConAcceptComponent chain link that returns true, and the component answers on the OR,
    // so the level range must not refuse the accept on its own.
    [Test]
    public async Task AnniversaryChain_SharedStartComponentStillAccepts()
    {
        var template = StartComponent(10542, 45935, out var component);
        component.ActTemplates.Add(new QuestActConAcceptComponent(component) { DetailId = 1120, QuestContextId = 10542 });
        component.ActTemplates.Add(new QuestActConAcceptLevelRange(component) { DetailId = 24, LevelMin = 54, LevelMax = 125 });

        await Assert.That(QuestAcceptLevelRangeRules.RefusesAccept(template, 20)).IsFalse();
        await Assert.That(QuestAcceptLevelRangeRules.RefusesAccept(template, 54)).IsFalse();
    }

    [Test]
    public async Task WithoutAStartRange_DoesNotRefuse()
    {
        var template = new QuestTemplate { Id = 1 };
        var progress = new QuestComponentTemplate(template) { Id = 5, KindId = QuestComponentKind.Progress };
        template.Components[progress.Id] = progress;

        await Assert.That(QuestAcceptLevelRangeRules.RefusesAccept(template, 55)).IsFalse();
        await Assert.That(QuestAcceptLevelRangeRules.RefusesAccept(null, 55)).IsFalse();
    }

    private static QuestTemplate StartComponent(uint questId, uint componentId, out QuestComponentTemplate component)
    {
        var template = new QuestTemplate { Id = questId };
        component = new QuestComponentTemplate(template) { Id = componentId, KindId = QuestComponentKind.Start };
        template.Components[component.Id] = component;
        return template;
    }
}
