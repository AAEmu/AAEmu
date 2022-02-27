using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncRespawn : DoodadPhaseFuncTemplate
    {
        public int MinTime { get; set; }
        public int MaxTime { get; set; }

        public override bool Use(Unit caster, Doodad owner)
        {
            _log.Trace("DoodadFuncRespawn: MinTime {0}, MaxTime {1}", MinTime, MaxTime);

            // Doodad spawn
            if (caster is Character character)
            {
                var spawnPos = character.Transform.Clone();
                spawnPos.Local.AddDistanceToFront(1f);
                spawnPos.Local.SetHeight(WorldManager.Instance.GetHeight(spawnPos));
                var doodad = new DoodadSpawner
                {
                    Id = 0,
                    UnitId = owner.TemplateId,
                    Position = spawnPos.CloneAsSpawnPosition()
                };
                doodad.Spawn(0);
            }

            return false;
        }
    }
}
