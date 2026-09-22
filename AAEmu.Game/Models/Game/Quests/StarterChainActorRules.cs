using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests;

/// <summary>
/// The NPC and doodad templates a quest chain needs standing in the world before a player can
/// accept, progress and report it. Value in, value out over act rows, so the six race starter
/// chains are pinned in tests and the World boot audit feeds it the same rows off the loaded
/// templates. Hunt targets count as NPCs: a kill objective needs its mob placed as much as a
/// talk objective needs its speaker.
/// </summary>
public static class StarterChainActorRules
{
    /// <summary>
    /// Highest level of a start-zone starter quest. Warborn 8160..8163 (quest_contexts.level 5,
    /// zone 227 e_sunny_wilderness_4) close the last first chain; the other five races' chains
    /// sit at level 1..3 (330/2531, 3484/3485, 2385..2387, 1112/1113, 1212/1213).
    /// </summary>
    public const byte StarterLevelCap = 5;

    /// <summary>One quest act row reduced to the ids that can name a world actor.</summary>
    public readonly record struct ActorAct(
        string DetailType,
        uint NpcId = 0,
        uint DoodadId = 0,
        uint HighlightDoodadId = 0);

    public readonly record struct RequiredActors(IReadOnlySet<uint> Npcs, IReadOnlySet<uint> Doodads);

    /// <summary>
    /// quest_contexts.race is one bit per enum_char_race id: 1 nuian, 4 dwarf, 8 elf,
    /// 16 hariharan, 32 ferre, 128 warborn; 255 is any race.
    /// </summary>
    public static byte RaceBit(Race race) =>
        race == Race.None ? (byte)0 : (byte)(1 << ((byte)race - 1));

    /// <summary>
    /// A quest is part of a race's starter chain when its race mask names that race alone, its
    /// level is within <see cref="StarterLevelCap"/> and its zone lies in the race's starting zone
    /// group. characters.starting_zone_id keys zones.zone_key; the warborn chain sits in
    /// e_sunny_wilderness_4 (zones.id 227), a sibling partition of the birth zone
    /// e_sunny_wilderness_1 (zone_key 157), so the zone group is the unit, not the zone.
    /// </summary>
    public static bool IsStarterChainQuest(
        byte questRaceMask,
        byte questLevel,
        uint questZoneGroupId,
        Race race,
        uint startZoneGroupId)
    {
        if (race == Race.None || questZoneGroupId == 0 || startZoneGroupId == 0)
            return false;
        return questRaceMask == RaceBit(race)
               && questLevel <= StarterLevelCap
               && questZoneGroupId == startZoneGroupId;
    }

    /// <summary>
    /// Actors the rows name. NPCs come from accept, accept-emotion, report, talk and hunt rows;
    /// doodads from accept, report, phase-check and interaction rows plus the highlight doodad of
    /// gather, use and interaction rows (the highlight is the object the objective is done on:
    /// 3485 gathers item 41439 from doodad 11641). Zero ids and every other act type are ignored.
    /// </summary>
    public static RequiredActors Collect(IEnumerable<ActorAct> acts)
    {
        var npcs = new SortedSet<uint>();
        var doodads = new SortedSet<uint>();
        foreach (var act in acts ?? Enumerable.Empty<ActorAct>())
        {
            switch (act.DetailType)
            {
                case nameof(QuestActConAcceptNpc):
                case nameof(QuestActConAcceptNpcEmotion):
                case nameof(QuestActConReportNpc):
                case nameof(QuestActObjTalk):
                case nameof(QuestActObjMonsterHunt):
                    AddNonZero(npcs, act.NpcId);
                    break;
                case nameof(QuestActConAcceptDoodad):
                case nameof(QuestActConReportDoodad):
                case nameof(QuestActObjDoodadPhaseCheck):
                    AddNonZero(doodads, act.DoodadId);
                    break;
                case nameof(QuestActObjInteraction):
                    AddNonZero(doodads, act.DoodadId);
                    AddNonZero(doodads, act.HighlightDoodadId);
                    break;
                case nameof(QuestActObjItemGather):
                case nameof(QuestActObjItemUse):
                    AddNonZero(doodads, act.HighlightDoodadId);
                    break;
            }
        }

        return new RequiredActors(npcs, doodads);
    }

    /// <summary>The actor-naming rows of a loaded template, in the shape <see cref="Collect"/> reads.</summary>
    public static IEnumerable<ActorAct> ActsOf(IQuestTemplate template)
    {
        if (template?.Components == null)
            yield break;

        foreach (var component in template.Components.Values)
        {
            if (component?.ActTemplates == null)
                continue;

            foreach (var act in component.ActTemplates)
            {
                switch (act)
                {
                    case QuestActConAcceptNpc a:
                        yield return new ActorAct(nameof(QuestActConAcceptNpc), NpcId: a.NpcId);
                        break;
                    case QuestActConAcceptNpcEmotion a:
                        yield return new ActorAct(nameof(QuestActConAcceptNpcEmotion), NpcId: a.NpcId);
                        break;
                    case QuestActConReportNpc a:
                        yield return new ActorAct(nameof(QuestActConReportNpc), NpcId: a.NpcId);
                        break;
                    case QuestActObjTalk a:
                        yield return new ActorAct(nameof(QuestActObjTalk), NpcId: a.NpcId);
                        break;
                    case QuestActObjMonsterHunt a:
                        yield return new ActorAct(nameof(QuestActObjMonsterHunt), NpcId: a.NpcId);
                        break;
                    case QuestActConAcceptDoodad a:
                        yield return new ActorAct(nameof(QuestActConAcceptDoodad), DoodadId: a.DoodadId);
                        break;
                    case QuestActConReportDoodad a:
                        yield return new ActorAct(nameof(QuestActConReportDoodad), DoodadId: a.DoodadId);
                        break;
                    case QuestActObjDoodadPhaseCheck a:
                        yield return new ActorAct(nameof(QuestActObjDoodadPhaseCheck), DoodadId: a.DoodadId);
                        break;
                    case QuestActObjInteraction a:
                        yield return new ActorAct(
                            nameof(QuestActObjInteraction),
                            DoodadId: a.DoodadId,
                            HighlightDoodadId: a.HighlightDoodadId);
                        break;
                    case QuestActObjItemGather a:
                        yield return new ActorAct(nameof(QuestActObjItemGather), HighlightDoodadId: a.HighlightDoodadId);
                        break;
                    case QuestActObjItemUse a:
                        yield return new ActorAct(nameof(QuestActObjItemUse), HighlightDoodadId: a.HighlightDoodadId);
                        break;
                }
            }
        }
    }

    private static void AddNonZero(ISet<uint> set, uint id)
    {
        if (id != 0)
            set.Add(id);
    }
}
