using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

/// <summary>
/// The nine Progress act types over their content rows: each occupies an objective slot and its
/// MaxObjective follows quest_acts count and quest_contexts.score the way the other Progress acts do.
/// </summary>
public class QuestProgressActTemplateTests
{
    private static QuestComponentTemplate Progress(uint questId, int score = 0)
    {
        var quest = new QuestTemplate { Id = questId, Score = score };
        return new QuestComponentTemplate(quest) { KindId = QuestComponentKind.Progress };
    }

    // quest_act_obj_monster_contr_group_hunts 2 and 11: hero quest 9118 (score 100), groups 907 (count 30)
    // and 887 (count 1).
    [Test]
    public async Task ContrGroupHunt_HeroQuest9118_WeighsGroupCountAgainstScore()
    {
        var strong = new QuestActObjMonsterContrGroupHunt(Progress(9118, 100))
        {
            QuestMonsterGroupId = 907, Count = 30, LongDist = true
        };
        var weak = new QuestActObjMonsterContrGroupHunt(Progress(9118, 100))
        {
            QuestMonsterGroupId = 887, Count = 1, LongDist = true
        };
        await Assert.That(strong.CountsAsAnObjective).IsTrue();
        await Assert.That(strong.MaxObjective()).IsEqualTo(4);
        await Assert.That(weak.MaxObjective()).IsEqualTo(101);
    }

    // quest_act_obj_monster_contr_hunts 13: quest 10737, npc 21204, count 1, score 0.
    [Test]
    public async Task ContrHunt_Quest10737_NeedsOneContribution()
    {
        var act = new QuestActObjMonsterContrHunt(Progress(10737)) { NpcId = 21204, Count = 1, LongDist = true };
        await Assert.That(act.CountsAsAnObjective).IsTrue();
        await Assert.That(act.MaxObjective()).IsEqualTo(1);
    }

    // quest_act_obj_effect_fires 15: quest 1388, effect 61085, count 3, team_share t.
    [Test]
    public async Task EffectFire_Quest1388_CountsThreeFires()
    {
        var act = new QuestActObjEffectFire(Progress(1388)) { EffectId = 61085, Count = 3, TeamShare = true };
        await Assert.That(act.CountsAsAnObjective).IsTrue();
        await Assert.That(act.MaxObjective()).IsEqualTo(3);
    }

    // quest_act_obj_item_group_gathers 20: quest 5490, group 9, count 10, cleanup t.
    [Test]
    public async Task ItemGroupGather_Quest5490_NeedsTenFromTheGroup()
    {
        var act = new QuestActObjItemGroupGather(Progress(5490)) { ItemGroupId = 9, Count = 10, Cleanup = true };
        await Assert.That(act.CountsAsAnObjective).IsTrue();
        await Assert.That(act.MaxObjective()).IsEqualTo(10);
    }

    // quest_act_obj_sell_backpack_goods 10: quest 9011, Tag 3197, group 795, count 2.
    [Test]
    public async Task SellBackpackGood_Quest9011_TagRowNeedsTwoSales()
    {
        var act = new QuestActObjSellBackpackGood(Progress(9011))
        {
            ContentItemType = QuestSellBackpackGoodRules.ContentTypeTag, ContentItemId = 3197,
            QuestMonsterGroupId = 795, Count = 2
        };
        await Assert.That(act.CountsAsAnObjective).IsTrue();
        await Assert.That(act.MaxObjective()).IsEqualTo(2);
    }

    // quest_act_obj_invite_team_factions 4: quest 6804, expedition, buff 13921, count 4.
    [Test]
    public async Task InviteTeamFaction_Quest6804_NeedsFourInvites()
    {
        var act = new QuestActObjInviteTeamFaction(Progress(6804))
        {
            InviteType = QuestActObjInviteType.Expedition, BuffId = 13921, Count = 4
        };
        await Assert.That(act.CountsAsAnObjective).IsTrue();
        await Assert.That(act.MaxObjective()).IsEqualTo(4);
    }

    // quest_act_obj_conditions 23 (quest 6774), quest_act_obj_faction_competitions 1 (quest 9871) and
    // quest_act_obj_conquest_wars 1 (quest 6572) have no count column: one slot, met once or held open.
    [Test]
    public async Task ConditionFactionCompetitionConquestWar_OccupyOneSlot()
    {
        QuestActTemplate[] acts =
        [
            new QuestActObjCondition(Progress(6774)) { ConditionId = QuestConditionObj.Fail, QuestContextId = 6621 },
            new QuestActObjFactionCompetition(Progress(9871)) { ZoneGroupId = 20, CompleteRank = 3 },
            new QuestActObjConquestWar(Progress(6572)) { ZoneGroupId = 78, CompleteRank = 4 }
        ];
        foreach (var act in acts)
        {
            await Assert.That(act.CountsAsAnObjective).IsTrue();
            await Assert.That(act.Count).IsEqualTo(1);
            await Assert.That(act.MaxObjective()).IsEqualTo(1);
        }
    }

    // QuestManager.Load registers every direct QuestActTemplate subclass of the Acts namespace as a
    // quest_acts.act_detail_type; AddActTemplate throws for a type outside that scan.
    [Test]
    public async Task EveryProgressActType_IsRegisteredByTheLoaderScan()
    {
        var registered = Helpers.GetTypesInNamespace(typeof(QuestManager).Assembly, "AAEmu.Game.Models.Game.Quests.Acts")
            .Where(type => type.BaseType == typeof(QuestActTemplate))
            .Select(type => type.Name)
            .ToHashSet();
        string[] expected =
        [
            nameof(QuestActObjEffectFire), nameof(QuestActObjItemGroupGather), nameof(QuestActObjCondition),
            nameof(QuestActObjMonsterContrHunt), nameof(QuestActObjMonsterContrGroupHunt),
            nameof(QuestActObjSellBackpackGood), nameof(QuestActObjInviteTeamFaction),
            nameof(QuestActObjFactionCompetition), nameof(QuestActObjConquestWar)
        ];
        foreach (var name in expected)
            await Assert.That(registered).Contains(name);
    }
}
