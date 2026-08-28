using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncRespawn : DoodadPhaseFuncTemplate
{
    public int MinTime { get; set; }
    public int MaxTime { get; set; }

    public override bool Use(BaseUnit caster, Doodad owner)
    {
        Logger.Trace("DoodadFuncRespawn: MinTime {0}, MaxTime {1}", MinTime, MaxTime);

        // Doodad spawn
        if (caster is Character character)
        {
            using var spawnPos = character.Transform.Clone();
            spawnPos.Local.AddDistanceToFront(1f);
            spawnPos.Local.SetHeight(caster.ParentWorld.Template.Floor.GetFloor(spawnPos.World.Position.X, spawnPos.World.Position.Y, spawnPos.World.Position.Z, FloorContext.Spawn)); // WorldManager.Instance.GetHeight(spawnPos));
            var doodad = new DoodadSpawner
            {
                ParentWorld = character.ParentWorld,
                Id = owner.ObjId,
                UnitId = owner.TemplateId,
                Position = spawnPos.CloneAsSpawnPosition()
            };
            doodad.Spawn(0);
        }

        return false;
    }
}
