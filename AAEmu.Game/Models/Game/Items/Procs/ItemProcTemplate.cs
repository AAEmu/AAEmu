using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Items.Procs;

/// <summary>
/// DB model for the table item_procs
/// </summary>
public class ItemProcTemplate
{
    public uint Id { get; set; }
    public uint SkillId { get; set; }
    public ProcChanceKind ChanceKind { get; set; }
    public uint ChanceRate { get; set; }
    public uint ChanceParam { get; set; } // Always zero in 1.2
    public uint CooldownSec { get; set; }
    public bool Finisher { get; set; }
    public uint ItemLevelBasedChanceBonus { get; set; }

    /// <summary><c>trigger_skill_id</c>: the one skill the proc answers (10 rows, all fire_skill). 0 is any skill.</summary>
    public uint TriggerSkillId { get; set; }

    /// <summary><c>trigger_tag_id</c>: a tag the skill behind the event must carry (6 rows, all tag 378). 0 is any.</summary>
    public uint TriggerTagId { get; set; }

    /// <summary><c>or_unit_reqs</c>: how the unit_reqs rows with owner_type "ItemProc" combine; 't' on proc 158 only.</summary>
    public bool OrUnitReqs { get; set; }

    public SkillTemplate SkillTemplate { get; set; }
}
