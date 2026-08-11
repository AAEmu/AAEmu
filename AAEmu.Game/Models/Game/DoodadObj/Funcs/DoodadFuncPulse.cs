using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncPulse : DoodadPhaseFuncTemplate
{
    public bool Flag { get; set; }

    /// <summary>
    /// Neighbourhood radius used when the spawner has no <c>RelatedIds</c>.
    /// Grimghast harpoons sit ~10–30 m from their mana trebuchet; 60 m covers both lines without
    /// whole-map side effects.
    /// </summary>
    private const float DefaultPulseRadius = 60f;

    public override bool Use(BaseUnit caster, Doodad owner)
    {
        Logger.Trace("DoodadFuncPulse: Flag {0} ownerTpl={1} obj={2}", Flag, owner?.TemplateId, owner?.ObjId);

        if (owner == null)
            return false;

        // Intermediate build steps pulse with Flag=false — no neighbour transit.
        if (!Flag)
            return false;

        var relatedIds = owner.Spawner?.RelatedIds;
        var hasRelatedFilter = relatedIds is { Count: > 0 };
        IEnumerable<Doodad> candidates;
        if (hasRelatedFilter)
        {
            var around = WorldManager.GetAround<Doodad>(owner);
            var related = relatedIds.ToHashSet();
            candidates = around.Where(d => related.Contains(d.TemplateId));
        }
        else
        {
            // Most main_world siege furniture (Grimghast treb → harpoons) never got RelatedIds authored
            // into doodad_spawns.json. Pulse by template proximity instead.
            candidates = WorldManager.GetAround<Doodad>(owner, DefaultPulseRadius);
        }

        var triggered = 0;
        foreach (var doodad in candidates)
        {
            if (doodad == null || doodad.ObjId == owner.ObjId)
                continue;

            var phaseFuncs = DoodadManager.Instance.GetPhaseFunc(doodad.FuncGroupId);
            var hasPulseTrigger = false;
            foreach (var func in phaseFuncs)
            {
                if (func.FuncType != "DoodadFuncPulseTrigger")
                    continue;
                hasPulseTrigger = true;
                // Allow the one-shot PulseTrigger on this shared phase-func row for this firing.
                func.PulseTriggered = false;
            }

            if (!hasPulseTrigger)
                continue;

            // Re-enter current phase so the DoodadFuncPulseTrigger can set OverridePhase → charged.
            doodad.DoChangePhase(caster, (int)doodad.FuncGroupId);
            triggered++;
        }

        if (triggered > 0)
        {
            Logger.Info(
                "DoodadFuncPulse ownerTpl={0} obj={1} flag={2} relatedFilter={3} charged={4}",
                owner.TemplateId, owner.ObjId, Flag, hasRelatedFilter, triggered);
        }

        return false;
    }
}
