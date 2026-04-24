using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncRatioRespawn : DoodadPhaseFuncTemplate
{
    public int Ratio { get; set; }
    public uint SpawnDoodadId { get; set; }

    public override bool Use(BaseUnit caster, Doodad owner)
    {
        Logger.Trace("DoodadFuncRatioRespawn : Ratio {0}, SpawnDoodadId {1}", Ratio, SpawnDoodadId);

        // Doodad respawn (template swap) via spawner.
        // In compact.sqlite3 this is used to drive data-driven cycles (e.g. sea weather marker -> active variant).
        // The phase-func execution loop stops on first "true", so we must perform the swap here (not only set a flag).
        if (owner.PhaseRatio <= Ratio && (owner.Spawner?.Id ?? 0) > 0)
        {
            var spawner = owner.Spawner;
            spawner.RespawnDoodadTemplateId = SpawnDoodadId;

            // Despawn the current doodad and immediately spawn the next template at the same spawner position.
            // This matches the expected behavior of RatioRespawn in content-driven doodad cycles.
            spawner.Despawn(owner);
            spawner.Spawn(0);

            return true; // Interrupt the PhaseFunc because we swapped the doodad
        }

        owner.CumulativePhaseRatio -= Ratio;
        return false;
    }
}
