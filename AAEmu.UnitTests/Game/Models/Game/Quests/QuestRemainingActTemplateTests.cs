using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

/// <summary>
/// The 14 Start, Ready and Reward act types over their content rows: each loads its row, takes no
/// objective slot, and the four that need a missing subsystem say so through IUnsupportedRewardAct.
/// </summary>
public class QuestRemainingActTemplateTests
{
    private static QuestComponentTemplate Component(uint questId, QuestComponentKind kind)
    {
        var quest = new QuestTemplate { Id = questId };
        return new QuestComponentTemplate(quest) { KindId = kind };
    }

    // quest_act_con_accept_npc_groups 1 (quest 7823, group 698) and quest_act_con_report_npc_groups 1
    // (quest 7823, group 702, use_alias t, alias 4832).
    [Test]
    public async Task NpcGroups_Quest7823_LoadTheirGroup()
    {
        var accept = new QuestActConAcceptNpcGroup(Component(7823, QuestComponentKind.Start)) { DetailId = 1, QuestMonsterGroupId = 698 };
        var report = new QuestActConReportNpcGroup(Component(7823, QuestComponentKind.Ready))
        {
            DetailId = 1, QuestMonsterGroupId = 702, UseAlias = true, QuestActObjAliasId = 4832
        };
        await Assert.That(accept.CountsAsAnObjective).IsFalse();
        await Assert.That(report.CountsAsAnObjective).IsFalse();
        await Assert.That(accept.QuestMonsterGroupId).IsEqualTo(698u);
        await Assert.That(report.QuestMonsterGroupId).IsEqualTo(702u);
        await Assert.That(report.ThisComponentObjectiveIndex).IsEqualTo(QuestObjectiveSlotRules.NoSlot);
    }

    // quest_act_con_accept_level_ranges 2 (quest 10930, 10..19) and quest_act_con_accept_buffs 30
    // (quest 9340, buff 24422).
    [Test]
    public async Task LevelRangeAndBuff_LoadTheirGate()
    {
        var range = new QuestActConAcceptLevelRange(Component(10930, QuestComponentKind.Start)) { DetailId = 2, LevelMin = 10, LevelMax = 19 };
        var buff = new QuestActConAcceptBuff(Component(9340, QuestComponentKind.Start)) { DetailId = 30, BuffId = 24422 };
        await Assert.That(range.CountsAsAnObjective).IsFalse();
        await Assert.That(buff.CountsAsAnObjective).IsFalse();
        await Assert.That(range.LevelMin).IsEqualTo(10);
        await Assert.That(range.LevelMax).IsEqualTo(19);
        await Assert.That(buff.BuffId).IsEqualTo(24422u);
    }

    // quest_act_supply_actabilities 2 (quest 4445, group 34, 50), quest_act_supply_leadership_points 2
    // (quest 2971, 30) and quest_act_supply_local_lps 2 (quest 11175, 150).
    [Test]
    public async Task PointSupplies_LoadTheirAmount()
    {
        var actability = new QuestActSupplyActability(Component(4445, QuestComponentKind.Reward)) { DetailId = 2, ActabilityGroupId = 34, Point = 50 };
        var leadership = new QuestActSupplyLeadershipPoint(Component(2971, QuestComponentKind.Reward)) { DetailId = 2, Point = 30 };
        var localLp = new QuestActSupplyLocalLp(Component(11175, QuestComponentKind.Reward)) { DetailId = 2, LocalLp = 150 };
        await Assert.That(actability.ActabilityGroupId).IsEqualTo(34u);
        await Assert.That(actability.Point).IsEqualTo(50);
        await Assert.That(leadership.Point).IsEqualTo(30);
        await Assert.That(localLp.LocalLp).IsEqualTo(150);
        await Assert.That(actability.CountsAsAnObjective || leadership.CountsAsAnObjective || localLp.CountsAsAnObjective).IsFalse();
    }

    // quest_act_supply_skills 14 (quest 10452, skill 46452) and quest_act_obj_send_mails 1 (quest 8952,
    // item 34820 x1, use_alias t, alias 6103).
    [Test]
    public async Task SkillAndMail_LoadTheirRow()
    {
        var skill = new QuestActSupplySkill(Component(10452, QuestComponentKind.Ready)) { DetailId = 14, SkillId = 46452 };
        var mail = new QuestActObjSendMail(Component(8952, QuestComponentKind.Start))
        {
            DetailId = 1, ItemId1 = 34820, Count1 = 1, UseAlias = true, QuestActObjAliasId = 6103
        };
        await Assert.That(skill.SkillId).IsEqualTo(46452u);
        await Assert.That(skill.CountsAsAnObjective).IsFalse();
        await Assert.That(mail.ItemId1).IsEqualTo(34820u);
        await Assert.That(mail.Count1).IsEqualTo(1);
        await Assert.That(mail.ItemId2).IsEqualTo(0u);
        await Assert.That(mail.CountsAsAnObjective).IsFalse();
    }

    // quest_act_supply_resident_charges 1 (quest 9334, zone group 33),
    // quest_act_supply_faction_changes 13 (quest 9902, 161, ignore_limit t),
    // quest_act_supply_ranked_items 1 (quest 6572, rank 1, 43779 x8) and
    // quest_act_supply_result_ranked_items 9 (quest 11132, win, rank 1, 51578 x15).
    // Resident points are not here: their 10, 15 or 30 are worth less than the rest of the Reward
    // step, so that act completes without them instead of refusing the accept.
    [Test]
    public async Task MissingSubsystemRewards_LoadAndNameWhatIsMissing()
    {
        QuestActTemplate[] acts =
        [
            new QuestActSupplyResidentCharge(Component(9334, QuestComponentKind.Reward)) { DetailId = 1, ZoneGroupId = 33, Charge = 10000 },
            new QuestActSupplyFactionChange(Component(9902, QuestComponentKind.Reward)) { DetailId = 13, SystemFactionId = 161, IgnoreLimit = true },
            new QuestActSupplyRankedItem(Component(6572, QuestComponentKind.Reward)) { DetailId = 1, Rank = 1, ItemId = 43779, GradeId = 0, Count = 8 },
            new QuestActSupplyResultRankedItem(Component(11132, QuestComponentKind.Reward)) { DetailId = 9, Result = true, Rank = 1, ItemId = 51578, Count = 15 }
        ];
        foreach (var act in acts)
        {
            await Assert.That(act is IUnsupportedRewardAct).IsTrue();
            await Assert.That(string.IsNullOrEmpty(((IUnsupportedRewardAct)act).MissingSubsystem)).IsFalse();
            await Assert.That(act.CountsAsAnObjective).IsFalse();
        }

        var ranked = (QuestActSupplyRankedItem)acts[2];
        await Assert.That(ranked.Count).IsEqualTo(8);
        await Assert.That(ranked.ItemId).IsEqualTo(43779u);
        var result = (QuestActSupplyResultRankedItem)acts[3];
        await Assert.That(result.Result).IsTrue();
        await Assert.That(result.Count).IsEqualTo(15);
    }

    [Test]
    public async Task ExecutableActs_AreNotMarkedUnsupported()
    {
        QuestActTemplate[] acts =
        [
            new QuestActConAcceptNpcGroup(Component(7823, QuestComponentKind.Start)),
            new QuestActConReportNpcGroup(Component(7823, QuestComponentKind.Ready)),
            new QuestActConAcceptLevelRange(Component(10930, QuestComponentKind.Start)),
            new QuestActConAcceptBuff(Component(9340, QuestComponentKind.Start)),
            new QuestActSupplyActability(Component(4445, QuestComponentKind.Reward)),
            new QuestActSupplyLeadershipPoint(Component(2971, QuestComponentKind.Reward)),
            new QuestActSupplyLocalLp(Component(11175, QuestComponentKind.Reward)),
            new QuestActSupplySkill(Component(10452, QuestComponentKind.Ready)),
            new QuestActObjSendMail(Component(8952, QuestComponentKind.Start)),
            new QuestActSupplyResidentPoint(Component(9334, QuestComponentKind.Reward)) { DetailId = 1, ZoneGroupId = 33, Point = 10 }
        ];
        foreach (var act in acts)
            await Assert.That(act is IUnsupportedRewardAct).IsFalse();

        // quest_act_supply_resident_points 1 (quest 9334, zone group 33): still loads its row.
        var residentPoints = (QuestActSupplyResidentPoint)acts[9];
        await Assert.That(residentPoints.ZoneGroupId).IsEqualTo(33u);
        await Assert.That(residentPoints.Point).IsEqualTo(10);
    }

    // QuestManager.Load registers every direct QuestActTemplate subclass of the Acts namespace as a
    // quest_acts.act_detail_type; AddActTemplate throws for a type outside that scan.
    [Test]
    public async Task EveryRemainingActType_IsRegisteredByTheLoaderScan()
    {
        var registered = Helpers.GetTypesInNamespace(typeof(QuestManager).Assembly, "AAEmu.Game.Models.Game.Quests.Acts")
            .Where(type => type.BaseType == typeof(QuestActTemplate))
            .Select(type => type.Name)
            .ToHashSet();
        string[] expected =
        [
            nameof(QuestActSupplyActability), nameof(QuestActConReportNpcGroup), nameof(QuestActConAcceptNpcGroup),
            nameof(QuestActSupplyLeadershipPoint), nameof(QuestActSupplyResidentPoint), nameof(QuestActSupplyFactionChange),
            nameof(QuestActConAcceptBuff), nameof(QuestActSupplyRankedItem), nameof(QuestActSupplyResultRankedItem),
            nameof(QuestActConAcceptLevelRange), nameof(QuestActObjSendMail), nameof(QuestActSupplyLocalLp),
            nameof(QuestActSupplySkill), nameof(QuestActSupplyResidentCharge)
        ];
        foreach (var name in expected)
            await Assert.That(registered).Contains(name);
    }
}
