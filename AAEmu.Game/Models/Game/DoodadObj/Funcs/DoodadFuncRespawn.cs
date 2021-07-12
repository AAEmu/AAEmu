using System;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncRespawn : DoodadFuncTemplate
    {
        public int MinTime { get; set; }
        public int MaxTime { get; set; }

        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            _log.Debug("DoodadFuncRespawn: MinTime {0}, MaxTime {1}", MinTime, MaxTime);

            owner.ToPhaseAndUse = false;

            // Doodad spawn
            if (!(caster is Character character))
            {
                return;
            }
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
    }
}
