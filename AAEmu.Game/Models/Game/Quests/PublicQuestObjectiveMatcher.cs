using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests;

public readonly record struct PublicQuestObjectiveProgress(byte ObjectiveIndex, int Delta, int Target);

public enum PublicQuestProgressEventKind : byte
{
    EvolvingMaterial,
    Honor,
    Living,
    Labor,
    NpcKill,
    MonsterGroupHunt,
    ItemUse,
    QuestComplete
}

/// <summary>A progress event captured before it leaves the character event callback.</summary>
public readonly record struct PublicQuestProgressEvent(
    uint ContributorId,
    uint ExpeditionId,
    PublicQuestProgressEventKind Kind,
    int Amount,
    uint ReferenceId,
    uint ActabilityGroupId,
    byte ContributorHeirLevel,
    byte KilledNpcLevel,
    NpcGradeType KilledNpcGrade)
{
    public static bool TryCapture(Character contributor, EventArgs source, out PublicQuestProgressEvent captured)
    {
        captured = default;
        if (contributor?.Expedition == null || source == null)
            return false;

        var expeditionId = (uint)contributor.Expedition.Id;
        captured = source switch
        {
            OnQuestProgressStatArgs { Kind: QuestProgressStatKind.EvolvingMaterial, Amount: > 0 } stat =>
                Create(PublicQuestProgressEventKind.EvolvingMaterial, stat.Amount),
            OnQuestProgressStatArgs { Kind: QuestProgressStatKind.Honor, Amount: > 0 } stat =>
                Create(PublicQuestProgressEventKind.Honor, stat.Amount),
            OnQuestProgressStatArgs { Kind: QuestProgressStatKind.Living, Amount: > 0 } stat =>
                Create(PublicQuestProgressEventKind.Living, stat.Amount),
            OnLaborPowerArgs { LaborUsed: > 0 } labor =>
                Create(PublicQuestProgressEventKind.Labor, labor.LaborUsed,
                    actabilityGroupId: labor.ActabilityGroupId),
            OnZoneKillArgs { Victim: Npc { Template: not null } npc } =>
                Create(PublicQuestProgressEventKind.NpcKill, 1,
                    contributorHeirLevel: contributor.HeirLevel,
                    killedNpcLevel: npc.Level,
                    killedNpcGrade: npc.Template.NpcGradeId),
            OnMonsterGroupHuntArgs { Count: > 0 and <= int.MaxValue } hunt =>
                Create(PublicQuestProgressEventKind.MonsterGroupHunt, (int)hunt.Count, hunt.NpcId),
            OnItemUseArgs used => Create(PublicQuestProgressEventKind.ItemUse, 1, used.ItemId),
            OnQuestCompleteArgs complete when complete.QuestId > 0 =>
                Create(PublicQuestProgressEventKind.QuestComplete, 1, complete.QuestId),
            _ => default
        };
        return captured.Amount > 0;

        PublicQuestProgressEvent Create(
            PublicQuestProgressEventKind kind,
            int amount,
            uint referenceId = 0,
            uint actabilityGroupId = 0,
            byte contributorHeirLevel = 0,
            byte killedNpcLevel = 0,
            NpcGradeType killedNpcGrade = default)
            => new(contributor.Id, expeditionId, kind, amount, referenceId, actabilityGroupId,
                contributorHeirLevel, killedNpcLevel, killedNpcGrade);
    }
}

/// <summary>Pure matching rules for content-backed expedition public objectives.</summary>
public static class PublicQuestObjectiveMatcher
{
    public static bool TryGetDefinition(QuestActTemplate objective, out byte objectiveIndex, out int target)
    {
        objectiveIndex = byte.MaxValue;
        target = 0;
        if (objective?.ParentQuestTemplate?.DetailId != QuestDetail.Expedition ||
            objective.ThisComponentObjectiveIndex == byte.MaxValue || objective.Count <= 0 ||
            objective is not (QuestActObjConsumeEvolvingMaterial or QuestActObjGainHonorPoint or
                QuestActObjGainLivingPoint or QuestActObjLaborPower or QuestActObjNpcKill or
                QuestActObjMonsterGroupHunt or QuestActObjItemGroupUse or QuestActObjCompleteQuestGroup))
            return false;
        objectiveIndex = objective.ThisComponentObjectiveIndex;
        target = objective.Count;
        return true;
    }

    public static bool TryGetProgress(
        QuestActTemplate objective,
        in PublicQuestProgressEvent progressEvent,
        IQuestManager questManager,
        out PublicQuestObjectiveProgress progress)
    {
        progress = default;
        if (progressEvent.Amount <= 0 || questManager == null ||
            !TryGetDefinition(objective, out var objectiveIndex, out var target))
            return false;

        var delta = objective switch
        {
            QuestActObjConsumeEvolvingMaterial when
                progressEvent.Kind == PublicQuestProgressEventKind.EvolvingMaterial => progressEvent.Amount,
            QuestActObjGainHonorPoint when progressEvent.Kind == PublicQuestProgressEventKind.Honor =>
                progressEvent.Amount,
            QuestActObjGainLivingPoint when progressEvent.Kind == PublicQuestProgressEventKind.Living =>
                progressEvent.Amount,
            QuestActObjLaborPower labor when progressEvent.Kind == PublicQuestProgressEventKind.Labor &&
                                                  (labor.ActabilityGroupId == 0 || labor.ActabilityGroupId ==
                                                   progressEvent.ActabilityGroupId)
                => progressEvent.Amount,
            QuestActObjNpcKill npcKill when progressEvent.Kind == PublicQuestProgressEventKind.NpcKill &&
                                                MatchesNpcKill(npcKill, progressEvent)
                => 1,
            QuestActObjMonsterGroupHunt group when
                progressEvent.Kind == PublicQuestProgressEventKind.MonsterGroupHunt &&
                progressEvent.ReferenceId == group.QuestMonsterGroupId => progressEvent.Amount,
            QuestActObjItemGroupUse itemGroup when progressEvent.Kind == PublicQuestProgressEventKind.ItemUse &&
                                                     questManager.CheckGroupItem(itemGroup.ItemGroupId,
                                                         progressEvent.ReferenceId)
                => 1,
            QuestActObjCompleteQuestGroup questGroup when
                progressEvent.Kind == PublicQuestProgressEventKind.QuestComplete &&
                                                               QuestProgressActRules.CountsTowardCompleteQuestGroup(
                                                                   progressEvent.ReferenceId,
                                                                   objective.ParentQuestTemplate.Id) &&
                                                               questManager.CheckContextGroup(
                                                                   questGroup.QuestContextGroupId,
                                                                   progressEvent.ReferenceId)
                => 1,
            _ => 0
        };
        if (delta <= 0)
            return false;

        progress = new PublicQuestObjectiveProgress(
            objectiveIndex,
            delta,
            target);
        return true;
    }

    private static bool MatchesNpcKill(QuestActObjNpcKill objective, in PublicQuestProgressEvent progressEvent)
    {
        return QuestProgressActRules.LevelInRange(progressEvent.ContributorHeirLevel,
                   objective.HeirLevelMin, objective.HeirLevelMax) &&
               QuestProgressActRules.LevelInRange(progressEvent.KilledNpcLevel,
                   objective.LevelMin, objective.LevelMax) &&
               QuestProgressActRules.NpcGradeAllowed(progressEvent.KilledNpcGrade,
                   objective.GradeNormal, objective.GradeStrong, objective.GradeElite,
                   objective.GradeBossA, objective.GradeBossB, objective.GradeBossC);
    }
}
