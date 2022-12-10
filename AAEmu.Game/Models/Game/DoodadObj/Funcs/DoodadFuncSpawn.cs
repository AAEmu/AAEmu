using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncSpawn : DoodadFuncTemplate
    {
        public bool DespawnOnCreatorDeath { get; set; }
        public float LifeTime { get; set; }
        public uint MateStateId { get; set; }
        public float OriAngle { get; set; }
        public uint OriDirId { get; set; }
        public uint OwnerTypeId { get; set; }
        public float PosAngleMax { get; set; }
        public float PosAngleMin { get; set; }
        public uint PosDirId { get; set; }
        public float PosDistanceMax { get; set; }
        public float PosDistanceMin { get; set; }
        public uint SubType { get; set; }
        public bool UseSummonerAggroTarget { get; set; }
        public bool UseSummonerFaction { get; set; }

        // doodad_funcs
        public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            _log.Debug("DoodadFuncSpawn");

            // Doodad spawn
            if (caster is Character character)
            {
                var spawnPos = character.Transform.Clone();
                spawnPos.Local.AddDistanceToFront(1f);
                spawnPos.Local.SetHeight(WorldManager.Instance.GetHeight(spawnPos));
                var doodad = new DoodadSpawner
                {
                    Id = owner.ObjId,
                    UnitId = owner.TemplateId,
                    Position = spawnPos.CloneAsSpawnPosition()
                };
                doodad.Spawn(0);
            }
        }
    }
}
