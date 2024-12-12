using System.Collections.Generic;
using AAEmu.Game.Models.Game.AI.Enums;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;

#pragma warning disable IDE0130 // Namespace does not match folder structure

namespace AAEmu.Game.Models.Game.Quests;

/// <summary>
/// One component of a quest
/// </summary>
public class QuestComponentTemplate(QuestTemplate parentTemplate)
{
    public uint Id { get; set; }
    public QuestComponentKind KindId { get; set; }
    /// <summary>
    /// NextComponent feels like it is a deprecated field in the compact.sqlite3, the only 3 references doesn't seem to make any sense
    /// </summary>
    public uint NextComponent { get; set; }
    public QuestNpcAiName NpcAiId { get; set; }
    public uint NpcId { get; set; }
    public uint SkillId { get; set; }
    public bool SkillSelf { get; set; }
    public string AiPathName { get; set; }
    public PathType AiPathTypeId { get; set; }
    public uint NpcSpawnerId { get; set; }
    public bool PlayCinemaBeforeBubble { get; set; }
    public uint AiCommandSetId { get; set; }
    public bool OrUnitReqs { get; set; }
    public uint CinemaId { get; set; }
    public uint BuffId { get; set; }
    public QuestTemplate ParentQuestTemplate { get; set; } = parentTemplate;
    public List<QuestActTemplate> ActTemplates { get; set; } = [];
}
