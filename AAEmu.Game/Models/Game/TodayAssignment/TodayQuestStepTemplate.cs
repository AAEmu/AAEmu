namespace AAEmu.Game.Models.Game.TodayAssignment;

public class TodayQuestStepTemplate
{
    public uint Id { get; set; }
    public uint RealStep { get; set; }
    /// <summary>Optional bag cost to unlock this step (from today_quest_steps.item_id / item_num).</summary>
    public uint ItemId { get; set; }
    public int ItemNum { get; set; }
    public bool OrUnitReqs { get; set; }
    public int LevelMin { get; set; }
    public int LevelMax { get; set; }

    /// <summary>Board this step is listed on (today_quest_steps.sort_id).</summary>
    public int SortId { get; set; }

    public List<TodayQuestGroupTemplate> Groups { get; } = [];

    /// <summary>The Hero board: only seated heroes see it, and level_min/level_max are hero grades.</summary>
    public const int HeroBoardSortId = 4;

    /// <summary>Arche Pass mission board (<c>enum_today_quest_sorts.arche_pass</c>).</summary>
    public const int ArchePassBoardSortId = 5;
    public const int ExpeditionBoardSortId = 2;
    public const int FamilyBoardSortId = 3;
    public const int ExpeditionPublicBoardSortId = 6;

    public bool IsHeroBoard => SortId == HeroBoardSortId;

    public bool IsArchePassBoard => SortId == ArchePassBoardSortId;
    public bool IsExpeditionPublicBoard => SortId == ExpeditionPublicBoardSortId;
}
