using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

/// <summary>
/// Drives doodads other than the one entering the phase. Used by the Auroria faction bases to switch
/// their rank rewards on and off: reaching Prosperous (rank 4) flips the faction's Trade Outlet and
/// its two Gilda Star merchants into their active phase, and the destroyed phase reverts them.
/// </summary>
/// <remarks>
/// <see cref="TargetDoodadId"/> is a doodad *template* id, not an object id, so every doodad of that
/// template in the same world is switched. In practice each template is faction-specific
/// (12329 Nuian Trade Outlet vs 12330 Haranyan), so this does not leak across factions.
/// The column is a varchar in the client data, but all 1010 rows hold a single plain integer or null.
/// </remarks>
public class DoodadFuncChangeOtherDoodadPhase : DoodadPhaseFuncTemplate
{
    // doodad_func_change_other_doodad_phases
    public uint TargetDoodadId { get; set; }
    public int TargetPhase { get; set; }
    public int NextPhase { get; set; }

    public override bool Use(BaseUnit caster, Doodad owner)
    {
        Logger.Trace("DoodadFuncChangeOtherDoodadPhase: TargetDoodadId {0}, TargetPhase {1}, ObjId {2}",
            TargetDoodadId, TargetPhase, owner.ObjId);

        if (TargetDoodadId > 0 && TargetPhase > 0 && owner.ParentWorld != null)
        {
            var changed = 0;
            // Snapshot the matches: changing a phase can spawn or despawn doodads
            foreach (var target in owner.ParentWorld.GetDoodadsByTemplateId(TargetDoodadId))
            {
                if (target == null || target.ObjId == owner.ObjId)
                    continue;

                target.DoChangePhase(caster, TargetPhase);
                changed++;
            }

            if (changed > 0)
            {
                Logger.Info($"DoodadFuncChangeOtherDoodadPhase: doodad {owner.TemplateId} (objId {owner.ObjId}) moved {changed} doodad(s) of template {TargetDoodadId} to phase {TargetPhase}");
            }
            else
            {
                Logger.Warn($"DoodadFuncChangeOtherDoodadPhase: doodad {owner.TemplateId} (objId {owner.ObjId}) found no doodad of template {TargetDoodadId} to move to phase {TargetPhase}");
            }
        }

        // A few rows carry no target and exist purely to move the owner on
        if (NextPhase > 0)
        {
            owner.OverridePhase = NextPhase;
            return true; // stop the remaining phase funcs so the override is taken
        }

        return false;
    }
}
