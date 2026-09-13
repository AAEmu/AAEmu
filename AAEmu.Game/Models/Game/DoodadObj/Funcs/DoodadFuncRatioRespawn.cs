using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncRatioRespawn : DoodadPhaseFuncTemplate
{
    public int Ratio { get; set; }
    public uint SpawnDoodadId { get; set; }

    public override bool Use(BaseUnit caster, Doodad owner)
    {
        var selected = owner.TrySelectWeightedPhaseRatio(Ratio);
        Logger.Trace("DoodadFuncRatioRespawn: Weight {0}, Roll {1}, UpperBound {2}, SpawnDoodadId {3}",
            Ratio, owner.PhaseRatio, owner.CumulativePhaseRatio, SpawnDoodadId);

        // Doodad spawn
        if (selected && (owner.Spawner?.Id ?? 0) > 0)
        {
            /*
            var doodad = DoodadManager.Instance.Create(0, SpawnDoodadId);
            doodad.Transform = owner.Transform.Clone();
            doodad.Spawn();
            owner.Delete();
            */
            owner.Spawner.RespawnDoodadTemplateId = SpawnDoodadId;

            return true; // Interrupt the PhaseFunc as new doodad is spawned
        }

        return false;
    }
}
