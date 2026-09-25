using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

/// <summary>
/// Marks the doodad's current phase as reacting to a Zone area edge.
/// <para>
/// The edge itself is owned by Zone and arrives as ZWEnterArea/ZWLeaveArea; the
/// dispatch that matches this row and completes the phase lives in
/// <c>DoodadAreaTriggerRuntime</c>. A direct F-use never reaches this row.
/// </para>
/// </summary>
public class DoodadFuncAreaTrigger : DoodadFuncTemplate
{
    // doodad_func_area_triggers
    /// <summary>
    /// Optional NPC template the edge must come from. Shipped rows leave this NULL,
    /// which means the owning doodad reacts to the edge itself.
    /// </summary>
    public uint NpcId { get; set; }

    /// <summary>Enter or leave; the shipped rows are all enter.</summary>
    public bool IsEnter { get; set; }

    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        Logger.Trace("DoodadFuncAreaTrigger");
    }
}
