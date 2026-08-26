using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Core.Managers.UnitManagers;

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
            if (!DoodadManager.Instance.Exist(SpawnDoodadId))
            {
                Logger.Error(
                    $"DoodadFuncRatioRespawn: Spawn template {SpawnDoodadId} does not exist (spawner={spawner.Id}, currentTemplate={owner.TemplateId}).");
                owner.CumulativePhaseRatio -= Ratio;
                return false;
            }

            spawner.RespawnDoodadTemplateId = SpawnDoodadId;

            // Despawn the current doodad and immediately spawn the next template at the same spawner position.
            // This matches the expected behavior of RatioRespawn in content-driven doodad cycles.
            spawner.Despawn(owner);
            var spawned = spawner.Spawn(0);
            if (spawned == null)
                Logger.Error($"DoodadFuncRatioRespawn: Spawn failed for template {SpawnDoodadId} at spawner {spawner.Id}.");

            return true; // Interrupt the PhaseFunc because we swapped the doodad
        }

        owner.CumulativePhaseRatio -= Ratio;
        return false;
    }
}
